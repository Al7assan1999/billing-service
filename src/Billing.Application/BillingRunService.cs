using Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Billing.Application;

/// <summary>
/// The scheduled billing run: renews every active subscription whose period has ended and issues the
/// next invoice. Safe to run repeatedly on the same day: a renewed subscription is no longer due, and a
/// unique index on (SubscriptionId, PeriodStart) rejects any duplicate invoice.
/// </summary>
public class BillingRunService(IBillingDbContext db, BillingService billing)
{
    public async Task<BillingRunResult> RunAsync(DateOnly today, CancellationToken ct)
    {
        var issued = 0;

        // A subscription several periods behind (e.g. the job was down) catches up one period per loop.
        while (true)
        {
            var due = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= today)
                .OrderBy(s => s.CurrentPeriodEnd)
                .Take(100)
                .ToListAsync(ct);
            if (due.Count == 0) break;

            var plans = await db.Plans
                .Where(p => due.Select(s => s.PlanId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            foreach (var subscription in due)
            {
                var invoice = subscription.Renew(plans[subscription.PlanId]);
                await billing.IssueAsync(invoice, ct);
                db.Invoices.Add(invoice);
                issued++;
            }
            await db.SaveChangesAsync(ct);
        }

        return new BillingRunResult(today, issued);
    }
}
