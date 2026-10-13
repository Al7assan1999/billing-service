using Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Billing.Application;

/// <summary>
/// The persistence the use cases need. Implemented by EF Core in Infrastructure; the application
/// layer queries it with LINQ instead of a repository per entity.
/// </summary>
public interface IBillingDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Invoice> Invoices { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IInvoiceNumberGenerator
{
    /// <summary>Unique, gap-tolerant, increasing invoice number, e.g. INV-2026-000042.</summary>
    Task<string> NextAsync(DateOnly issueDate, CancellationToken ct = default);
}

public interface IInvoicePdfRenderer
{
    byte[] Render(Invoice invoice, Customer customer);
}

public class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>VAT applied to every invoice (15% is the Saudi standard rate).</summary>
    public decimal TaxRate { get; set; } = 0.15m;
    public int PaymentTermsDays { get; set; } = 14;
}

public class NotFoundException(string entity, Guid id) : Exception($"{entity} {id} was not found.");
