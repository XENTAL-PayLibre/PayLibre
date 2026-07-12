using System.Security.Claims;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Api.Auth;

/// <summary>Resolves the current tenant (school) + user from the authenticated JWT.</summary>
public sealed class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid RequireTenantId() =>
        TenantId ?? throw new AuthenticationException("No authenticated tenant on the request.");

    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? accessor.HttpContext?.User.FindFirst("sub")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserEmail =>
        accessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? accessor.HttpContext?.User.FindFirst("email")?.Value;
}
