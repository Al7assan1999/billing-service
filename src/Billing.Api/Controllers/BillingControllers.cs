using Billing.Application;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(BillingService billing) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CustomerDto>> List(CancellationToken ct) => billing.ListCustomersAsync(ct);

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        var customer = await billing.CreateCustomerAsync(request, ct);
        return Created($"/api/customers/{customer.Id}", customer);
    }
}

[ApiController]
[Route("api/plans")]
public class PlansController(BillingService billing) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PlanDto>> List(CancellationToken ct) => billing.ListPlansAsync(ct);

    [HttpPost]
    public async Task<ActionResult<PlanDto>> Create(CreatePlanRequest request, CancellationToken ct)
    {
        var plan = await billing.CreatePlanAsync(request, ct);
        return Created($"/api/plans/{plan.Id}", plan);
    }

    /// <summary>Stops new subscriptions to the plan; existing subscribers keep it.</summary>
    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
    {
        await billing.RetirePlanAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/subscriptions")]
public class SubscriptionsController(BillingService billing) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SubscriptionDto>> List(CancellationToken ct) => billing.ListSubscriptionsAsync(ct);

    /// <summary>Starts a subscription and issues the invoice for the first month.</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Start(StartSubscriptionRequest request, CancellationToken ct)
    {
        var (subscription, invoice) = await billing.StartSubscriptionAsync(request, ct);
        return Created($"/api/subscriptions/{subscription.Id}", new { subscription, invoice });
    }

    /// <summary>Switches plan now and issues a prorated invoice for the rest of the period.</summary>
    [HttpPost("{id:guid}/change-plan")]
    public Task<InvoiceDto> ChangePlan(Guid id, ChangePlanRequest request, CancellationToken ct) => billing.ChangePlanAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await billing.CancelSubscriptionAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/invoices")]
public class InvoicesController(BillingService billing, IInvoicePdfRenderer pdf) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<InvoiceDto>> List([FromQuery] InvoiceQuery query, CancellationToken ct) => billing.ListInvoicesAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<InvoiceDto> Get(Guid id, CancellationToken ct) => billing.GetInvoiceAsync(id, ct);

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (invoice, customer) = await billing.GetInvoiceForPdfAsync(id, ct);
        return File(pdf.Render(invoice, customer), "application/pdf", $"{invoice.Number ?? "draft"}.pdf");
    }

    [HttpPost("{id:guid}/payments")]
    public Task<InvoiceDto> RecordPayment(Guid id, RecordPaymentRequest request, CancellationToken ct) => billing.RecordPaymentAsync(id, request, ct);

    [HttpPost("{id:guid}/void")]
    public Task<InvoiceDto> Void(Guid id, VoidInvoiceRequest request, CancellationToken ct) => billing.VoidInvoiceAsync(id, request, ct);
}

[ApiController]
[Route("api")]
public class OperationsController(BillingService billing, BillingRunService run, TimeProvider time) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardSummary> Dashboard(CancellationToken ct) => billing.GetDashboardAsync(ct);

    /// <summary>
    /// Triggers the billing run now (it also runs hourly in the background). A date can be passed to
    /// simulate a future run, which is how the integration tests move time forward.
    /// </summary>
    [HttpPost("billing-runs")]
    public Task<BillingRunResult> RunBilling([FromQuery] DateOnly? date, CancellationToken ct) =>
        run.RunAsync(date ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), ct);
}
