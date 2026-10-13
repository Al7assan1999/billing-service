namespace Billing.Domain;

public class Customer : Entity
{
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer() { }

    public Customer(string name, string email, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Customer name is required.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new DomainException("A valid email is required.");
        Name = name.Trim();
        Email = email.Trim().ToLowerInvariant();
        CreatedAt = createdAt;
    }
}

public class Plan : Entity
{
    public string Name { get; private set; } = null!;
    public decimal MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    private Plan() { }

    public Plan(string name, decimal monthlyPrice, string currency)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Plan name is required.");
        if (monthlyPrice <= 0) throw new DomainException("Monthly price must be positive.");
        if (currency is not { Length: 3 }) throw new DomainException("Currency must be a 3-letter ISO code.");
        Name = name.Trim();
        MonthlyPrice = Money.Round(monthlyPrice);
        Currency = currency.ToUpperInvariant();
    }

    public void Retire() => IsActive = false;
}
