namespace Billing.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>A business rule was violated. The API maps this to 409/422-style problem responses.</summary>
public class DomainException(string message) : Exception(message);

public static class Money
{
    /// <summary>Banking rounding for display amounts: 2 decimals, half away from zero.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
