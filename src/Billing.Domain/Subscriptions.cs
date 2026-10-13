namespace Billing.Domain;

public enum SubscriptionStatus { Active, Cancelled }

/// <summary>
/// A customer on a plan, billed monthly in advance. A billing period is
/// [CurrentPeriodStart, CurrentPeriodEnd): the end date is the first day of the next period.
/// </summary>
public class Subscription : Entity
{
    public Guid CustomerId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateOnly StartedOn { get; private set; }
    public DateOnly CurrentPeriodStart { get; private set; }
    public DateOnly CurrentPeriodEnd { get; private set; }
    public DateOnly? CancelledOn { get; private set; }

    private Subscription() { }

    private Subscription(Guid customerId, Plan plan, DateOnly startDate)
    {
        if (!plan.IsActive) throw new DomainException($"Plan '{plan.Name}' is no longer available.");
        CustomerId = customerId;
        PlanId = plan.Id;
        Status = SubscriptionStatus.Active;
        StartedOn = startDate;
        CurrentPeriodStart = startDate;
        CurrentPeriodEnd = startDate.AddMonths(1);
    }

    /// <summary>Starts the subscription and returns the invoice for its first period.</summary>
    public static (Subscription Subscription, Invoice FirstInvoice) Start(Guid customerId, Plan plan, DateOnly startDate)
    {
        var subscription = new Subscription(customerId, plan, startDate);
        return (subscription, subscription.PeriodInvoice(plan));
    }

    public bool IsDueForRenewal(DateOnly today) => Status == SubscriptionStatus.Active && today >= CurrentPeriodEnd;

    /// <summary>Moves to the next period and returns its invoice. Called by the billing run.</summary>
    public Invoice Renew(Plan plan)
    {
        if (Status != SubscriptionStatus.Active) throw new DomainException("Only active subscriptions renew.");
        if (plan.Id != PlanId) throw new DomainException("Renewal must use the subscription's current plan.");
        CurrentPeriodStart = CurrentPeriodEnd;
        CurrentPeriodEnd = CurrentPeriodStart.AddMonths(1);
        return PeriodInvoice(plan);
    }

    /// <summary>
    /// Switches plan immediately. The unused part of the current period is credited at the old price
    /// and charged at the new price, on one invoice (negative total = net credit).
    /// </summary>
    public Invoice ChangePlan(Plan current, Plan next, DateOnly today)
    {
        if (Status != SubscriptionStatus.Active) throw new DomainException("Cancelled subscriptions can't change plan.");
        if (current.Id != PlanId) throw new DomainException("Current plan doesn't match the subscription.");
        if (next.Id == PlanId) throw new DomainException("Already on this plan.");
        if (!next.IsActive) throw new DomainException($"Plan '{next.Name}' is no longer available.");
        if (next.Currency != current.Currency) throw new DomainException("Plans must use the same currency.");
        if (today < CurrentPeriodStart || today >= CurrentPeriodEnd) throw new DomainException("Change date must fall in the current period.");

        var remaining = Proration.RemainingFraction(CurrentPeriodStart, CurrentPeriodEnd, today);
        var invoice = new Invoice(CustomerId, next.Currency, InvoiceKind.Proration, Id, today, CurrentPeriodEnd);
        invoice.AddLine($"Unused time on {current.Name} ({today:yyyy-MM-dd} to {CurrentPeriodEnd:yyyy-MM-dd})", 1, -Money.Round(current.MonthlyPrice * remaining));
        invoice.AddLine($"Remaining time on {next.Name} ({today:yyyy-MM-dd} to {CurrentPeriodEnd:yyyy-MM-dd})", 1, Money.Round(next.MonthlyPrice * remaining));

        PlanId = next.Id;
        return invoice;
    }

    public void Cancel(DateOnly today)
    {
        if (Status == SubscriptionStatus.Cancelled) throw new DomainException("Subscription is already cancelled.");
        Status = SubscriptionStatus.Cancelled;
        CancelledOn = today;
    }

    private Invoice PeriodInvoice(Plan plan)
    {
        var invoice = new Invoice(CustomerId, plan.Currency, InvoiceKind.Subscription, Id, CurrentPeriodStart, CurrentPeriodEnd);
        invoice.AddLine($"{plan.Name} ({CurrentPeriodStart:yyyy-MM-dd} to {CurrentPeriodEnd:yyyy-MM-dd})", 1, plan.MonthlyPrice);
        return invoice;
    }
}

public static class Proration
{
    /// <summary>Share of the period still unused on <paramref name="date"/>, by calendar days.</summary>
    public static decimal RemainingFraction(DateOnly periodStart, DateOnly periodEnd, DateOnly date)
    {
        var total = periodEnd.DayNumber - periodStart.DayNumber;
        var remaining = periodEnd.DayNumber - date.DayNumber;
        return (decimal)remaining / total;
    }
}
