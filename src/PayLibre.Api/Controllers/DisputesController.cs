using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Payments;
using PayLibre.Domain.Payments;

namespace PayLibre.Api.Controllers;

/// <summary>
/// The school's payment-dispute queue. Parents raise disputes from their app; staff review + resolve
/// them here. Read is open to all dashboard roles; resolving requires write (Owner/Admin/Bursar).
/// </summary>
[ApiController]
[Route("api/v1/disputes")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class DisputesController(DisputeService disputes, AuditService audit) : ControllerBase
{
    /// <summary>List disputes. <c>openOnly=true</c> (default) shows just the unresolved ones.</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.StaffRead)] // exposes parent emails/payment refs — not for ClassTeacher
    [ProducesResponseType(typeof(IEnumerable<DisputeResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DisputeResponse>>> List([FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        Ok((await disputes.ListAsync(openOnly, ct)).Select(ToResponse));

    /// <summary>Resolve (accept) or reject a dispute, with an optional note.</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(typeof(DisputeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeResponse>> Resolve(Guid id, ResolveDisputeRequest request, CancellationToken ct)
    {
        var d = await disputes.ResolveAsync(id, request.Accepted, request.Resolution, ct);
        await audit.RecordAsync("dispute.resolved", "PaymentDispute", d.Id,
            $"{(request.Accepted ? "Accepted" : "Rejected")} dispute on payment {d.PaymentId}.", ct);
        return Ok(ToResponse(d));
    }

    private static DisputeResponse ToResponse(PaymentDispute d) => new(
        d.Id, d.PaymentId, d.Status.ToString(), d.RaisedByEmail, d.Reason,
        d.Resolution, d.ResolvedByEmail, d.CreatedAtUtc, d.ResolvedAtUtc);
}
