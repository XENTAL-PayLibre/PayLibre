namespace PayLibre.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "paylibre";
    public string Audience { get; set; } = "paylibre-dashboard";
    /// <summary>HMAC signing key (>= 32 bytes). Supplied via secret/env in real envs.</summary>
    public string SigningKey { get; set; } = string.Empty;
    /// <summary>Dashboard access-token lifetime (short; paired with a rotating refresh token).</summary>
    public int AccessTokenLifetimeSeconds { get; set; } = 900;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    /// <summary>Bcrypt work factor (>= 12).</summary>
    public int BcryptWorkFactor { get; set; } = 12;
    /// <summary>Whether auth cookies require HTTPS (Secure flag). True in real environments.</summary>
    public bool CookieSecure { get; set; } = true;
    /// <summary>SameSite policy: "Lax" (default), "None", or "Strict". "None" forces Secure.</summary>
    public string CookieSameSite { get; set; } = "Lax";
    /// <summary>Cookie domain (e.g. ".paylibre..."). Empty = host-only.</summary>
    public string CookieDomain { get; set; } = string.Empty;
}
