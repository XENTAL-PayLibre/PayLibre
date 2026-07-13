using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Payments;
using PayLibre.Domain.Payments;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Maker-checker refunds of received payments. One Owner/Admin requests a refund; a <b>different</b>
/// Owner/Admin approves it, which executes the refund against Xental. Every step is written to the
/// audit trail. Money-out, so all mutating endpoints require the manage-school policy.
/// </summary>
[ApiController]
[Route("api/v1/refunds")]
public sealed class RefundsController(RefundService refunds, AuditService audit) : ControllerBase
{
    /// <summary>List refund requests (most recent first).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.StaffRead)] // money data — not for ClassTeacher
    [ProducesResponseType(typeof(IEnumerable<RefundRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RefundRequestResponse>>> List(CancellationToken ct) =>
        Ok((await refunds.ListAsync(ct)).Select(ToResponse));

    /// <summary>Request a refund for a received payment (step 1 of 2).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(RefundRequestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundRequestResponse>> Request(CreateRefundRequest request, CancellationToken ct)
    {
        var refund = await refunds.RequestAsync(request.PaymentId, request.Reason, ct);
        await audit.RecordAsync("refund.requested", "Payment", refund.PaymentId,
            $"Requested a refund for payment {refund.PaymentId}.", ct);
        return Created($"/api/v1/refunds/{refund.Id}", ToResponse(refund));
    }

    /// <summary>Approve a pending refund (step 2) — must be a different user than the requester. Executes it.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(RefundRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RefundRequestResponse>> Approve(Guid id, CancellationToken ct)
    {
        var refund = await refunds.ApproveAsync(id, ct);
        await audit.RecordAsync("refund.executed", "Payment", refund.PaymentId,
            $"Approved + executed a refund of {refund.AmountKobo} kobo for payment {refund.PaymentId}.", ct);
        return Ok(ToResponse(refund));
    }

    /// <summary>Reject a pending refund.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(RefundRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RefundRequestResponse>> Reject(Guid id, RejectRefundRequest request, CancellationToken ct)
    {
        var refund = await refunds.RejectAsync(id, request.Note, ct);
        await audit.RecordAsync("refund.rejected", "Payment", refund.PaymentId,
            $"Rejected a refund for payment {refund.PaymentId}.", ct);
        return Ok(ToResponse(refund));
    }

    private static RefundRequestResponse ToResponse(RefundRequest r) => new(
        r.Id, r.PaymentId, r.Status.ToString(), r.RequestedByEmail, r.DecidedByEmail,
        r.Reason, r.DecisionNote, r.AmountKobo, r.FailureReason, r.CreatedAtUtc, r.DecidedAtUtc);
}
