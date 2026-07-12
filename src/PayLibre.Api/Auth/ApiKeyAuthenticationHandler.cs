using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PayLibre.Api.Authorization;
using PayLibre.Application.ApiKeys;

namespace PayLibre.Api.Auth;

/// <summary>
/// Authenticates a school's public-API request from an <c>X-Api-Key</c> header (or
/// <c>Authorization: Bearer plb_…</c>). On success it builds a principal carrying the owning school as
/// <c>tenant_id</c> (so the normal tenant row-filter applies) plus a claim per granted scope.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string ScopeClaim = "apiscope";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(presented))
        {
            var auth = Request.Headers.Authorization.FirstOrDefault();
            if (auth?.StartsWith("Bearer plb_", StringComparison.Ordinal) == true) presented = auth["Bearer ".Length..];
        }
        if (string.IsNullOrEmpty(presented)) return AuthenticateResult.NoResult();

        var svc = Context.RequestServices.GetRequiredService<ApiKeyService>();
        var key = await svc.AuthenticateAsync(presented);
        if (key is null) return AuthenticateResult.Fail("Invalid API key.");

        var claims = new List<Claim>
        {
            new("tenant_id", key.SchoolId.ToString()),
            new(AuthPolicies.ScopeClaim, "api"),
            new("api_key_id", key.Id.ToString()),
        };
        foreach (var scope in key.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ScopeClaim, scope));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
