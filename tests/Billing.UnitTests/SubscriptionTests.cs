using Billing.Domain;

namespace Billing.UnitTests;

public class SubscriptionTests
{
    private static readonly Plan Basic = new("Basic", 100m, "SAR");
    private static readonly Plan Pro = new("Pro", 200m, "SAR");
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    [Fact]
    public void Starting_bills_the_first_month_in_advance()
    {
        var (sub, invoice) = Subscription.Start(Customer, Basic, Jan1);

        Assert.Equal(new DateOnly(2026, 2, 1), sub.CurrentPeriodEnd);
        Assert.Equal(InvoiceKind.Subscription, invoice.Kind);
        Assert.Equal(Jan1, invoice.PeriodStart);
        Assert.Equal(100m, Assert.Single(invoice.Lines).Amount);
    }

    [Fact]
    public void Renewal_moves_to_the_next_period_and_bills_it()
    {
        var (sub, _) = Subscription.Start(Customer, Basic, new DateOnly(2026, 1, 31));

        Assert.False(sub.IsDueForRenewal(new DateOnly(2026, 2, 27)));
        Assert.True(sub.IsDueForRenewal(new DateOnly(2026, 2, 28)));

        var invoice = sub.Renew(Basic);

        // Jan 31 + 1 month clamps to Feb 28; the next period starts there.
        Assert.Equal(new DateOnly(2026, 2, 28), invoice.PeriodStart);
        Assert.Equal(new DateOnly(2026, 3, 28), sub.CurrentPeriodEnd);
    }

    [Fact]
    public void Upgrade_mid_period_credits_unused_time_and_charges_the_new_plan()
    {
        var (sub, _) = Subscription.Start(Customer, Basic, Jan1);

        // Jan 11: 21 of January's 31 days remain.
        var invoice = sub.ChangePlan(Basic, Pro, new DateOnly(2026, 1, 11));

        Assert.Equal(Pro.Id, sub.PlanId);
        Assert.Equal(InvoiceKind.Proration, invoice.Kind);
        Assert.Equal([-67.74m, 135.48m], invoice.Lines.Select(l => l.Amount));
    }

    [Fact]
    public void Plan_change_must_happen_inside_the_current_period()
    {
        var (sub, _) = Subscription.Start(Customer, Basic, Jan1);

        Assert.Throws<DomainException>(() => sub.ChangePlan(Basic, Pro, new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Cancelled_subscriptions_are_not_renewed()
    {
        var (sub, _) = Subscription.Start(Customer, Basic, Jan1);
        sub.Cancel(new DateOnly(2026, 1, 20));

        Assert.False(sub.IsDueForRenewal(new DateOnly(2026, 3, 1)));
        Assert.Throws<DomainException>(() => sub.Renew(Basic));
        Assert.Throws<DomainException>(() => sub.Cancel(new DateOnly(2026, 1, 21)));
    }

    [Fact]
    public void Retired_plans_cannot_be_subscribed_to()
    {
        var legacy = new Plan("Legacy", 50m, "SAR");
        legacy.Retire();

        Assert.Throws<DomainException>(() => Subscription.Start(Customer, legacy, Jan1));
    }

    [Theory]
    [InlineData("2026-01-01", 1.0)]
    [InlineData("2026-01-16", 0.5161290322580645161290322581)]
    [InlineData("2026-01-31", 0.0322580645161290322580645161)]
    public void Remaining_fraction_counts_calendar_days(string date, double expected) =>
        Assert.Equal((decimal)expected, Proration.RemainingFraction(Jan1, new DateOnly(2026, 2, 1), DateOnly.Parse(date)), 10);
}
