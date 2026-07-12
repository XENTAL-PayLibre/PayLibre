using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.ApiKeys;
using PayLibre.Application.Audit;
using PayLibre.Domain.ApiKeys;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Manage the school's API keys for the public API (SIS / website integration). Owner/Admin only. The
/// full key value is shown once at creation and never again — store it securely.
/// </summary>
[ApiController]
[Route("api/v1/api-keys")]
public sealed class ApiKeysController(ApiKeyService keys, AuditService audit) : ControllerBase
{
    /// <summary>List the school's API keys (prefixes + scopes only; secrets are never returned).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(IEnumerable<ApiKeyResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ApiKeyResponse>>> List(CancellationToken ct) =>
        Ok((await keys.ListAsync(ct)).Select(ToResponse));

    /// <summary>Create a scoped API key. The plaintext key is returned once in this response.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(CreatedApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreatedApiKeyResponse>> Create(CreateApiKeyRequest request, CancellationToken ct)
    {
        var issued = await keys.CreateAsync(request.Name, request.Scopes, ct);
        await audit.RecordAsync("apikey.created", "ApiKey", issued.Key.Id,
            $"Created API key \"{issued.Key.Name}\" ({issued.Key.Scopes}).", ct);
        return Created($"/api/v1/api-keys/{issued.Key.Id}", new CreatedApiKeyResponse(ToResponse(issued.Key), issued.PlaintextKey));
    }

    /// <summary>Revoke an API key (immediate).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await keys.RevokeAsync(id, ct);
        await audit.RecordAsync("apikey.revoked", "ApiKey", id, $"Revoked API key {id}.", ct);
        return NoContent();
    }

    private static ApiKeyResponse ToResponse(ApiKey k) => new(
        k.Id, k.Name, k.KeyPrefix, k.Scopes, k.IsActive, k.CreatedAtUtc, k.LastUsedAtUtc, k.RevokedAtUtc);
}
