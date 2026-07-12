using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;

namespace PayLibre.Api.Controllers;

/// <summary>
/// The school's audit trail — an immutable, tenant-scoped log of significant actions (fee changes,
/// settlement/late-fee updates, refunds, …). Read-only. Restricted to Owner/Admin (and, once added,
/// the Auditor role) via the manage-school policy.
/// </summary>
[ApiController]
[Route("api/v1/audit")]
public sealed class AuditController(AuditService audit) : ControllerBase
{
    /// <summary>Most-recent audit events first. <c>take</c> caps the page size (default 100, max 500).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.ViewAudit)]
    [ProducesResponseType(typeof(IEnumerable<AuditEventResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AuditEventResponse>>> List([FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok((await audit.ListAsync(take, ct)).Select(e => new AuditEventResponse(
            e.Id, e.CreatedAtUtc, e.ActorEmail, e.Action, e.EntityType, e.EntityId, e.Summary)));
}
