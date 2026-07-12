using PayLibre.Domain.Common;

namespace PayLibre.Domain.ApiKeys;

/// <summary>
/// A scoped API key a school issues to its own systems (SIS, website) to call PayLibre's public API —
/// create/sync students, read balances. The full secret is shown once at creation; only its hash and a
/// short prefix (for lookup + display) are stored. Tenant-owned. Revocable.
/// </summary>
public sealed class ApiKey : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Name { get; private set; } = null!;
    public string KeyPrefix { get; private set; } = null!;  // public, e.g. "plb_ab12cd34" (indexed for lookup)
    public string KeyHash { get; private set; } = null!;    // SHA-256 hex of the full key
    public string Scopes { get; private set; } = null!;     // comma-separated, e.g. "students:read,students:write"
    public DateTimeOffset? LastUsedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    private ApiKey() { }

    public ApiKey(Guid schoolId, string name, string keyPrefix, string keyHash, string scopes)
    {
        SchoolId = schoolId;
        Name = DomainException.Require(name, nameof(name));
        KeyPrefix = DomainException.Require(keyPrefix, nameof(keyPrefix));
        KeyHash = DomainException.Require(keyHash, nameof(keyHash));
        Scopes = scopes ?? string.Empty;
    }

    public bool HasScope(string scope) =>
        Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(scope, StringComparer.OrdinalIgnoreCase);

    public void MarkUsed(DateTimeOffset now) => LastUsedAtUtc = now;
    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}
