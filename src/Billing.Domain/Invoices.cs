namespace Billing.Domain;

public enum InvoiceStatus { Draft, Issued, Paid, Void }

/// <summary>Why the invoice exists. Only one Subscription invoice may exist per subscription period.</summary>
public enum InvoiceKind { Manual, Subscription, Proration }

public class Invoice : Entity
{
    public string? Number { get; private set; }
    public Guid CustomerId { get; private set; }
    public InvoiceKind Kind { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }
    public string Currency { get; private set; } = null!;
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? DueOn { get; private set; }
    public DateOnly? PaidOn { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }
    public string? VoidReason { get; private set; }

    private readonly List<InvoiceLine> _lines = [];
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public decimal AmountDue => Status == InvoiceStatus.Issued ? Total - AmountPaid : 0;

    private Invoice() { }

    public Invoice(Guid customerId, string currency, InvoiceKind kind = InvoiceKind.Manual, Guid? subscriptionId = null, DateOnly? periodStart = null, DateOnly? periodEnd = null)
    {
        CustomerId = customerId;
        Kind = kind;
        Currency = currency;
        SubscriptionId = subscriptionId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    public void AddLine(string description, int quantity, decimal unitPrice)
    {
        EnsureStatus(InvoiceStatus.Draft, "add lines to");
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        _lines.Add(new InvoiceLine(description, quantity, Money.Round(unitPrice)));
    }

    /// <summary>Finalises the amounts and makes the invoice payable. Lines can't change afterwards.</summary>
    public void Issue(string number, DateOnly today, decimal taxRate, int paymentTermsDays)
    {
        EnsureStatus(InvoiceStatus.Draft, "issue");
        if (_lines.Count == 0) throw new DomainException("An invoice needs at least one line.");
        if (taxRate is < 0 or > 1) throw new DomainException("Tax rate must be between 0 and 1.");

        Number = number;
        IssuedOn = today;
        DueOn = today.AddDays(paymentTermsDays);
        TaxRate = taxRate;
        Subtotal = Money.Round(_lines.Sum(l => l.Amount));
        Tax = Money.Round(Subtotal * taxRate);
        Total = Subtotal + Tax;
        Status = InvoiceStatus.Issued;

        // A net-credit invoice (e.g. downgrade proration) has nothing to collect.
        if (Total <= 0) MarkPaid(today);
    }

    public void RecordPayment(decimal amount, DateOnly date)
    {
        EnsureStatus(InvoiceStatus.Issued, "pay");
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount > AmountDue) throw new DomainException($"Payment of {amount} exceeds the amount due ({AmountDue}).");

        AmountPaid += amount;
        if (AmountPaid == Total) MarkPaid(date);
    }

    public void Void(string reason)
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Void) throw new DomainException($"A {Status} invoice can't be voided.");
        if (AmountPaid > 0) throw new DomainException("Invoices with payments can't be voided; issue a credit instead.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A reason is required.");
        VoidReason = reason;
        Status = InvoiceStatus.Void;
    }

    public bool IsOverdue(DateOnly today) => Status == InvoiceStatus.Issued && today > DueOn;

    private void MarkPaid(DateOnly date)
    {
        Status = InvoiceStatus.Paid;
        PaidOn = date;
    }

    private void EnsureStatus(InvoiceStatus expected, string action)
    {
        if (Status != expected) throw new DomainException($"Can't {action} an invoice that is {Status}.");
    }
}

public class InvoiceLine
{
    public int Id { get; private set; }
    public string Description { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount => Quantity * UnitPrice;

    private InvoiceLine() { }

    internal InvoiceLine(string description, int quantity, decimal unitPrice)
    {
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
