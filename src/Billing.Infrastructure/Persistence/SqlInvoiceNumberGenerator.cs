using Billing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Invoice numbers from a SQL Server SEQUENCE: unique and increasing across concurrent requests and
/// instances without locking a counter row.
/// </summary>
public class SqlInvoiceNumberGenerator(BillingDbContext db) : IInvoiceNumberGenerator
{
    public async Task<string> NextAsync(DateOnly issueDate, CancellationToken ct = default)
    {
        // Plain ADO.NET command: EF's SqlQuery composes raw SQL into a subquery for operators like Single(),
        // and SQL Server doesn't allow NEXT VALUE FOR inside one.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT NEXT VALUE FOR {BillingDbContext.InvoiceNumberSequence}";
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var next = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            return $"INV-{issueDate.Year}-{next:D6}";
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
