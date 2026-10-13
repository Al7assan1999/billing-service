using Billing.Application;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Persistence;

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options), IBillingDbContext
{
    public const string InvoiceNumberSequence = "InvoiceNumbers";

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>(InvoiceNumberSequence);

        b.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Email).HasMaxLength(320);
            e.HasIndex(c => c.Email).IsUnique();
        });

        b.Entity<Plan>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.MonthlyPrice).HasPrecision(18, 2);
            e.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        });

        b.Entity<Subscription>(e =>
        {
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => new { s.Status, s.CurrentPeriodEnd }); // billing run lookup
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(i => i.Number).HasMaxLength(32);
            e.HasIndex(i => i.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(i => i.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(i => i.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(i => i.VoidReason).HasMaxLength(500);
            foreach (var p in new[] { nameof(Invoice.Subtotal), nameof(Invoice.Tax), nameof(Invoice.Total), nameof(Invoice.AmountPaid) })
                e.Property(p).HasPrecision(18, 2);
            e.Property(i => i.TaxRate).HasPrecision(5, 4);
            e.Ignore(i => i.AmountDue);

            e.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Subscription>().WithMany().HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.Restrict);

            // The database, not just the code, guarantees one period invoice per subscription period.
            e.HasIndex(i => new { i.SubscriptionId, i.PeriodStart })
                .IsUnique()
                .HasFilter("[Kind] = 'Subscription'");
            e.HasIndex(i => new { i.Status, i.DueOn }); // overdue queries

            e.OwnsMany(i => i.Lines, l =>
            {
                l.ToTable("InvoiceLines");
                l.WithOwner().HasForeignKey("InvoiceId");
                l.HasKey(x => x.Id);
                l.Property(x => x.Description).HasMaxLength(300);
                l.Property(x => x.UnitPrice).HasPrecision(18, 2);
                l.Ignore(x => x.Amount);
            });
            e.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
