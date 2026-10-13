using Billing.Domain;

namespace Billing.UnitTests;

public class InvoiceTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private static Invoice Draft(params decimal[] amounts)
    {
        var invoice = new Invoice(Guid.NewGuid(), "SAR");
        foreach (var a in amounts) invoice.AddLine("Item", 1, a);
        return invoice;
    }

    [Fact]
    public void Issuing_adds_vat_and_sets_the_due_date()
    {
        var invoice = Draft(100m, 33.33m);

        invoice.Issue("INV-2026-000001", Today, taxRate: 0.15m, paymentTermsDays: 14);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(133.33m, invoice.Subtotal);
        Assert.Equal(20.00m, invoice.Tax);
        Assert.Equal(153.33m, invoice.Total);
        Assert.Equal(new DateOnly(2026, 3, 15), invoice.DueOn);
        Assert.Equal(153.33m, invoice.AmountDue);
    }

    [Fact]
    public void Issued_invoices_are_immutable()
    {
        var invoice = Draft(10m);
        invoice.Issue("INV-1", Today, 0.15m, 14);

        Assert.Throws<DomainException>(() => invoice.AddLine("Late addition", 1, 5m));
        Assert.Throws<DomainException>(() => invoice.Issue("INV-2", Today, 0.15m, 14));
    }

    [Fact]
    public void Empty_invoices_cannot_be_issued() =>
        Assert.Throws<DomainException>(() => Draft().Issue("INV-1", Today, 0.15m, 14));

    [Fact]
    public void Partial_then_full_payment_marks_paid()
    {
        var invoice = Draft(100m);
        invoice.Issue("INV-1", Today, 0.15m, 14);

        invoice.RecordPayment(15m, Today);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(100m, invoice.AmountDue);

        invoice.RecordPayment(100m, Today.AddDays(3));
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(Today.AddDays(3), invoice.PaidOn);
        Assert.Equal(0m, invoice.AmountDue);
    }

    [Fact]
    public void Overpayment_is_refused()
    {
        var invoice = Draft(100m);
        invoice.Issue("INV-1", Today, 0.15m, 14);

        Assert.Throws<DomainException>(() => invoice.RecordPayment(115.01m, Today));
    }

    [Fact]
    public void Net_credit_invoice_is_settled_on_issue()
    {
        var invoice = Draft(-67.74m, 30m); // downgrade proration

        invoice.Issue("INV-1", Today, 0.15m, 14);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.True(invoice.Total < 0);
    }

    [Fact]
    public void Overdue_only_after_the_due_date_while_unpaid()
    {
        var invoice = Draft(100m);
        invoice.Issue("INV-1", Today, 0.15m, 14);

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 3, 15)));
        Assert.True(invoice.IsOverdue(new DateOnly(2026, 3, 16)));

        invoice.RecordPayment(115m, new DateOnly(2026, 3, 20));
        Assert.False(invoice.IsOverdue(new DateOnly(2026, 3, 20)));
    }

    [Fact]
    public void Paid_or_partially_paid_invoices_cannot_be_voided()
    {
        var invoice = Draft(100m);
        invoice.Issue("INV-1", Today, 0.15m, 14);
        invoice.RecordPayment(10m, Today);

        Assert.Throws<DomainException>(() => invoice.Void("customer disputed"));

        var unpaid = Draft(50m);
        unpaid.Issue("INV-2", Today, 0.15m, 14);
        unpaid.Void("issued by mistake");
        Assert.Equal(InvoiceStatus.Void, unpaid.Status);
        Assert.Equal(0m, unpaid.AmountDue);
    }
}
