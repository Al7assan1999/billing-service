using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Application;
using Billing.Domain;

namespace Billing.IntegrationTests;

[Collection(nameof(SqlServerCollection))]
public class BillingApiTests(SqlServerFixture sql)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record StartResponse(SubscriptionDto Subscription, InvoiceDto Invoice);

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<(PlanDto Plan, CustomerDto Customer)> Seed(HttpClient api, decimal price = 100m, string email = "ops@acme.test")
    {
        var plan = await Read<PlanDto>(await api.PostAsJsonAsync("/api/plans", new CreatePlanRequest("Basic", price, "SAR")));
        var customer = await Read<CustomerDto>(await api.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Acme Ltd", email)));
        return (plan, customer);
    }

    [DockerFact]
    public async Task Subscription_lifecycle_from_first_invoice_to_monthly_renewals()
    {
        var api = sql.CreateApiClient();
        var (plan, customer) = await Seed(api);

        // Start: first month invoiced immediately with 15% VAT.
        var start = await Read<StartResponse>(await api.PostAsJsonAsync("/api/subscriptions",
            new StartSubscriptionRequest(customer.Id, plan.Id, new DateOnly(2026, 1, 15))));
        Assert.Equal(115.00m, start.Invoice.Total);
        Assert.Equal(InvoiceStatus.Issued, start.Invoice.Status);
        Assert.Matches(@"^INV-\d{4}-\d{6}$", start.Invoice.Number);

        // Pay it.
        var paid = await Read<InvoiceDto>(await api.PostAsJsonAsync($"/api/invoices/{start.Invoice.Id}/payments", new RecordPaymentRequest(115m, null)));
        Assert.Equal(InvoiceStatus.Paid, paid.Status);

        // Billing run on the renewal date issues exactly one invoice, and running again issues none.
        Assert.Equal(1, (await Read<BillingRunResult>(await api.PostAsync("/api/billing-runs?date=2026-02-15", null))).InvoicesIssued);
        Assert.Equal(0, (await Read<BillingRunResult>(await api.PostAsync("/api/billing-runs?date=2026-02-15", null))).InvoicesIssued);

        // If the job was down for two months, the next run catches up period by period.
        Assert.Equal(2, (await Read<BillingRunResult>(await api.PostAsync("/api/billing-runs?date=2026-04-20", null))).InvoicesIssued);

        var invoices = await Read<PagedResult<InvoiceDto>>(await api.GetAsync($"/api/invoices?customerId={customer.Id}&pageSize=50"));
        Assert.Equal(4, invoices.TotalCount);
        Assert.Equal(
            ["2026-01-15", "2026-02-15", "2026-03-15", "2026-04-15"],
            invoices.Items.Select(i => i.PeriodStart!.Value.ToString("yyyy-MM-dd")).Order());
        Assert.Equal(4, invoices.Items.Select(i => i.Number).Distinct().Count());
    }

    [DockerFact]
    public async Task Upgrading_issues_a_prorated_invoice()
    {
        var api = sql.CreateApiClient();
        var (basic, customer) = await Seed(api, price: 100m);
        var pro = await Read<PlanDto>(await api.PostAsJsonAsync("/api/plans", new CreatePlanRequest("Pro", 200m, "SAR")));
        var start = await Read<StartResponse>(await api.PostAsJsonAsync("/api/subscriptions",
            new StartSubscriptionRequest(customer.Id, basic.Id, Today.AddDays(-10))));

        var proration = await Read<InvoiceDto>(await api.PostAsJsonAsync($"/api/subscriptions/{start.Subscription.Id}/change-plan", new ChangePlanRequest(pro.Id)));

        var remaining = Proration.RemainingFraction(start.Subscription.CurrentPeriodStart, start.Subscription.CurrentPeriodEnd, Today);
        var expectedSubtotal = Money.Round(200m * remaining) - Money.Round(100m * remaining);
        Assert.Equal(2, proration.Lines.Count);
        Assert.Equal(expectedSubtotal, proration.Subtotal);
        Assert.Equal(Money.Round(expectedSubtotal * 0.15m), proration.Tax);
    }

    [DockerFact]
    public async Task Business_rule_violations_return_problem_details()
    {
        var api = sql.CreateApiClient();
        var (plan, customer) = await Seed(api);
        var start = await Read<StartResponse>(await api.PostAsJsonAsync("/api/subscriptions", new StartSubscriptionRequest(customer.Id, plan.Id, null)));

        var overpay = await api.PostAsJsonAsync($"/api/invoices/{start.Invoice.Id}/payments", new RecordPaymentRequest(1000m, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overpay.StatusCode);
        Assert.Contains("exceeds the amount due", await overpay.Content.ReadAsStringAsync());

        var duplicate = await api.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Acme again", "OPS@acme.test"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/invoices/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/plans", new { name = "", monthlyPrice = -1, currency = "X" })).StatusCode);
    }

    [DockerFact]
    public async Task Invoice_pdf_is_generated()
    {
        var api = sql.CreateApiClient();
        var (plan, customer) = await Seed(api);
        var start = await Read<StartResponse>(await api.PostAsJsonAsync("/api/subscriptions", new StartSubscriptionRequest(customer.Id, plan.Id, null)));

        var response = await api.GetAsync($"/api/invoices/{start.Invoice.Id}/pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [DockerFact]
    public async Task Dashboard_reports_mrr_outstanding_and_overdue()
    {
        var api = sql.CreateApiClient();
        var (plan, customer) = await Seed(api, price: 250m);
        // Started 40 days ago: the first invoice (issued today, due in 14 days) is outstanding but not overdue yet.
        await Read<StartResponse>(await api.PostAsJsonAsync("/api/subscriptions", new StartSubscriptionRequest(customer.Id, plan.Id, Today.AddDays(-40))));

        var summary = await Read<DashboardSummary>(await api.GetAsync("/api/dashboard"));

        Assert.Equal(1, summary.ActiveSubscriptions);
        Assert.Equal(250m, summary.MonthlyRecurringRevenue);
        Assert.Equal(287.50m, summary.Outstanding);
        Assert.Equal(0, summary.OverdueInvoices);
        Assert.Equal(6, summary.RevenueLast6Months.Count);
    }
}
