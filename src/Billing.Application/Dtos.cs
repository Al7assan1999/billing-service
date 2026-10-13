using System.ComponentModel.DataAnnotations;
using Billing.Domain;

namespace Billing.Application;

public record CreateCustomerRequest([Required, StringLength(200)] string Name, [Required, EmailAddress] string Email);
public record CustomerDto(Guid Id, string Name, string Email, DateTimeOffset CreatedAt);

public record CreatePlanRequest([Required, StringLength(100)] string Name, [Range(0.01, 1_000_000)] decimal MonthlyPrice, [Required, StringLength(3, MinimumLength = 3)] string Currency);
public record PlanDto(Guid Id, string Name, decimal MonthlyPrice, string Currency, bool IsActive);

public record StartSubscriptionRequest(Guid CustomerId, Guid PlanId, DateOnly? StartDate);
public record ChangePlanRequest(Guid PlanId);
public record SubscriptionDto(Guid Id, Guid CustomerId, string CustomerName, Guid PlanId, string PlanName, SubscriptionStatus Status,
    DateOnly StartedOn, DateOnly CurrentPeriodStart, DateOnly CurrentPeriodEnd, DateOnly? CancelledOn);

public record InvoiceLineDto(string Description, int Quantity, decimal UnitPrice, decimal Amount);
public record InvoiceDto(Guid Id, string? Number, Guid CustomerId, string CustomerName, InvoiceStatus Status, bool IsOverdue, string Currency,
    DateOnly? IssuedOn, DateOnly? DueOn, DateOnly? PaidOn, DateOnly? PeriodStart, DateOnly? PeriodEnd,
    decimal Subtotal, decimal TaxRate, decimal Tax, decimal Total, decimal AmountPaid, decimal AmountDue, IReadOnlyList<InvoiceLineDto> Lines);

public record InvoiceQuery(InvoiceStatus? Status = null, bool? Overdue = null, Guid? CustomerId = null, int Page = 1, int PageSize = 20);
public record RecordPaymentRequest([Range(0.01, 100_000_000)] decimal Amount, DateOnly? Date);
public record VoidInvoiceRequest([Required, StringLength(500)] string Reason);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record BillingRunResult(DateOnly RunDate, int InvoicesIssued);

public record MonthlyRevenue(string Month, decimal Amount);
public record DashboardSummary(
    int ActiveSubscriptions,
    decimal MonthlyRecurringRevenue,
    decimal Outstanding,
    int OverdueInvoices,
    decimal OverdueAmount,
    IReadOnlyList<MonthlyRevenue> RevenueLast6Months);
