namespace PayLibre.Domain.Common;

/// <summary>Raised when a domain invariant is violated. Mapped to HTTP 409 at the edge.</summary>
public sealed class DomainException(string message) : Exception(message)
{
    /// <summary>Guard: require a non-empty string, returning the trimmed value.</summary>
    public static string Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
        return value.Trim();
    }
}
