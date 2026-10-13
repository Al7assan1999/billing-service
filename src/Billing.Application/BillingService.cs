using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Billing.Application;

/// <summary>Use cases for customers, plans, subscriptions and invoices.</summary>
public class BillingService(IBillingDbContext db, IInvoiceNumberGenerator numbers, IOptions<BillingOptions> options, TimeProvider time)
{
    private readonly BillingOptions _options = options.Value;

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    // ---- Customers & plans ----

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest r, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.Email == r.Email.Trim().ToLower(), ct))
            throw new DomainException($"A customer with email {r.Email} already exists.");

        var customer = new Customer(r.Name, r.Email, time.GetUtcNow());
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    public async Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(CancellationToken ct) =>
        (await db.Customers.OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest r, CancellationToken ct)
    {
        var plan = new Plan(r.Name, r.MonthlyPrice, r.Currency);
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct) =>
        (await db.Plans.OrderBy(p => p.MonthlyPrice).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task RetirePlanAsync(Guid id, CancellationToken ct)
    {
        (await Find(db.Plans, id, ct)).Retire();
        await db.SaveChangesAsync(ct);
    }

    // ---- Subscriptions ----

    public async Task<(SubscriptionDto Subscription, InvoiceDto Invoice)> StartSubscriptionAsync(StartSubscriptionRequest r, CancellationToken ct)
    {
        var customer = await Find(db.Customers, r.CustomerId, ct);
        var plan = await Find(db.Plans, r.PlanId, ct);

        var (subscription, invoice) = Subscription.Start(customer.Id, plan, r.StartDate ?? Today);
        await IssueAsync(invoice, ct);

        db.Subscriptions.Add(subscription);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return (ToDto(subscription, customer, plan), ToDto(invoice, customer));
    }

    public async Task<InvoiceDto> ChangePlanAsync(Guid subscriptionId, ChangePlanRequest r, CancellationToken ct)
    {
        var subscription = await Find(db.Subscriptions, subscriptionId, ct);
        var current = await Find(db.Plans, subscription.PlanId, ct);
        var next = await Find(db.Plans, r.PlanId, ct);

        var invoice = subscription.ChangePlan(current, next, Today);
        await IssueAsync(invoice, ct);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return ToDto(invoice, await Find(db.Customers, subscription.CustomerId, ct));
    }

    public async Task CancelSubscriptionAsync(Guid id, CancellationToken ct)
    {
        (await Find(db.Subscriptions, id, ct)).Cancel(Today);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(CancellationToken ct)
    {
        var rows = await (
            from s in db.Subscriptions
            join c in db.Customers on s.CustomerId equals c.Id
            join p in db.Plans on s.PlanId equals p.Id
            orderby s.StartedOn descending
            select new { s, c, p }).ToListAsync(ct);
        return rows.Select(x => ToDto(x.s, x.c, x.p)).ToList();
    }

    // ---- Invoices ----

    public async Task<PagedResult<InvoiceDto>> ListInvoicesAsync(InvoiceQuery q, CancellationToken ct)
    {
        var today = Today;
        var query = db.Invoices.AsQueryable();
        if (q.Status is { } status) query = query.Where(i => i.Status == status);
        if (q.CustomerId is { } customerId) query = query.Where(i => i.CustomerId == customerId);
        if (q.Overdue == true) query = query.Where(i => i.Status == InvoiceStatus.Issued && i.DueOn < today);

        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var page = Math.Max(q.Page, 1);
        var total = await query.CountAsync(ct);
        var rows = await (
            from i in query
            join c in db.Customers on i.CustomerId equals c.Id
            orderby i.IssuedOn descending, i.Number descending
            select new { i, c })
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<InvoiceDto>(rows.Select(x => ToDto(x.i, x.c)).ToList(), page, pageSize, total);
    }

    public async Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct)
    {
        var invoice = await Find(db.Invoices, id, ct);
        return ToDto(invoice, await Find(db.Customers, invoice.CustomerId, ct));
    }

    public async Task<(Invoice Invoice, Customer Customer)> GetInvoiceForPdfAsync(Guid id, CancellationToken ct)
    {
        var invoice = await Find(db.Invoices, id, ct);
        return (invoice, await Find(db.Customers, invoice.CustomerId, ct));
    }

    public async Task<InvoiceDto> RecordPaymentAsync(Guid id, RecordPaymentRequest r, CancellationToken ct)
    {
        var invoice = await Find(db.Invoices, id, ct);
        invoice.RecordPayment(r.Amount, r.Date ?? Today);
        await db.SaveChangesAsync(ct);
        return ToDto(invoice, await Find(db.Customers, invoice.CustomerId, ct));
    }

    public async Task<InvoiceDto> VoidInvoiceAsync(Guid id, VoidInvoiceRequest r, CancellationToken ct)
    {
        var invoice = await Find(db.Invoices, id, ct);
        invoice.Void(r.Reason);
        await db.SaveChangesAsync(ct);
        return ToDto(invoice, await Find(db.Customers, invoice.CustomerId, ct));
    }

    // ---- Dashboard ----

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken ct)
    {
        var today = Today;
        var active = db.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active);
        var mrr = await active.Join(db.Plans, s => s.PlanId, p => p.Id, (s, p) => p.MonthlyPrice).SumAsync(ct);
        var issued = db.Invoices.Where(i => i.Status == InvoiceStatus.Issued);
        var overdue = issued.Where(i => i.DueOn < today);

        var since = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var paid = await db.Invoices
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidOn >= since)
            .Select(i => new { i.PaidOn, i.Total })
            .ToListAsync(ct);
        var revenue = Enumerable.Range(0, 6)
            .Select(m => since.AddMonths(m))
            .Select(month => new MonthlyRevenue(
                month.ToString("yyyy-MM"),
                paid.Where(p => p.PaidOn!.Value.Year == month.Year && p.PaidOn.Value.Month == month.Month).Sum(p => p.Total)))
            .ToList();

        return new DashboardSummary(
            await active.CountAsync(ct),
            mrr,
            await issued.SumAsync(i => i.Total - i.AmountPaid, ct),
            await overdue.CountAsync(ct),
            await overdue.SumAsync(i => i.Total - i.AmountPaid, ct),
            revenue);
    }

    // ---- Shared ----

    internal async Task IssueAsync(Invoice invoice, CancellationToken ct) =>
        invoice.Issue(await numbers.NextAsync(Today, ct), Today, _options.TaxRate, _options.PaymentTermsDays);

    private static async Task<T> Find<T>(DbSet<T> set, Guid id, CancellationToken ct) where T : Entity =>
        await set.FindAsync([id], ct) ?? throw new NotFoundException(typeof(T).Name, id);

    private static CustomerDto ToDto(Customer c) => new(c.Id, c.Name, c.Email, c.CreatedAt);
    private static PlanDto ToDto(Plan p) => new(p.Id, p.Name, p.MonthlyPrice, p.Currency, p.IsActive);

    private static SubscriptionDto ToDto(Subscription s, Customer c, Plan p) =>
        new(s.Id, c.Id, c.Name, p.Id, p.Name, s.Status, s.StartedOn, s.CurrentPeriodStart, s.CurrentPeriodEnd, s.CancelledOn);

    private InvoiceDto ToDto(Invoice i, Customer c) => new(
        i.Id, i.Number, c.Id, c.Name, i.Status, i.IsOverdue(Today), i.Currency,
        i.IssuedOn, i.DueOn, i.PaidOn, i.PeriodStart, i.PeriodEnd,
        i.Subtotal, i.TaxRate, i.Tax, i.Total, i.AmountPaid, i.AmountDue,
        i.Lines.Select(l => new InvoiceLineDto(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList());
}
