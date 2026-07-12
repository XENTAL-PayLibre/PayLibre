using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.ApiKeys;

namespace PayLibre.Application.ApiKeys;

/// <summary>The known API-key scopes for the public API.</summary>
public static class ApiScopes
{
    public const string StudentsRead = "students:read";
    public const string StudentsWrite = "students:write";
    public const string PaymentsRead = "payments:read";
    public static readonly string[] All = { StudentsRead, StudentsWrite, PaymentsRead };
}

public sealed record IssuedApiKey(ApiKey Key, string PlaintextKey);

/// <summary>
/// Issues, lists, revokes and authenticates a school's API keys. The plaintext key is returned only at
/// creation; only a hash + short prefix are stored. Authentication is tenant-agnostic (runs before the
/// tenant is known) and resolves the owning school + scopes from the presented key.
/// </summary>
public sealed class ApiKeyService(IApplicationDbContext db, ITenantContext tenant, IClock clock)
{
    private const string Prefix = "plb_";

    public async Task<IssuedApiKey> CreateAsync(string name, IReadOnlyList<string> scopes, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ValidationException("A key name is required.");
        var normalized = (scopes ?? Array.Empty<string>())
            .Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (normalized.Count == 0) throw new ValidationException("At least one scope is required.");
        var unknown = normalized.Except(ApiScopes.All, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0) throw new ValidationException($"Unknown scope(s): {string.Join(", ", unknown)}.");

        var plaintext = Prefix + Rand(12) + Rand(40);   // "plb_" + 12-char prefix body + 40-char secret
        var keyPrefix = plaintext[..16];                 // "plb_" + first 12 chars
        var key = new ApiKey(schoolId, name, keyPrefix, Sha256(plaintext), string.Join(",", normalized));
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return new IssuedApiKey(key, plaintext);
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return (await db.ApiKeys.AsNoTracking().ToListAsync(ct)).OrderByDescending(k => k.CreatedAtUtc).ToList();
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new NotFoundException("API key not found.");
        key.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resolve + validate a presented key (tenant-agnostic). Returns the active key or null.
    /// Updates last-used opportunistically.</summary>
    public async Task<ApiKey?> AuthenticateAsync(string presentedKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey) || presentedKey.Length < 16 || !presentedKey.StartsWith(Prefix))
            return null;
        var prefix = presentedKey[..16];
        var candidate = await db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && k.RevokedAtUtc == null, ct);
        if (candidate is null) return null;

        var providedHash = Sha256(presentedKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedHash), Encoding.UTF8.GetBytes(candidate.KeyHash)))
            return null;

        candidate.MarkUsed(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return candidate;
    }

    private static string Rand(int chars)
    {
        var bytes = RandomNumberGenerator.GetBytes(chars);
        return Convert.ToBase64String(bytes).Replace('+', 'a').Replace('/', 'b').TrimEnd('=')[..chars];
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
