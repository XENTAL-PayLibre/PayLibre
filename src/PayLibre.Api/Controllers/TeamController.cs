using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Schools;
using PayLibre.Domain.Schools;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Staff / team management. Owner/Admin invite members by email + role; the invitee accepts via the
/// emailed link (anonymous) to create their account, then signs in normally. Roles: Admin and Bursar
/// can write; Accountant and Auditor are read-only (Auditor also sees the audit trail).
/// </summary>
[ApiController]
[Route("api/v1/team")]
public sealed class TeamController(InviteService invites, AuditService audit) : ControllerBase
{
    /// <summary>List invitations (most recent first).</summary>
    [HttpGet("invites")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(IEnumerable<InviteResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<InviteResponse>>> ListInvites(CancellationToken ct) =>
        Ok((await invites.ListAsync(ct)).Select(ToResponse));

    /// <summary>Invite a staff member (Owner/Admin). Emails an invitation link.</summary>
    [HttpPost("invites")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(InviteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InviteResponse>> CreateInvite(CreateInviteRequest request, CancellationToken ct)
    {
        var role = Enum.Parse<SchoolRole>(request.Role, ignoreCase: true);
        var invite = await invites.CreateAsync(request.Email, role, request.ClassIds, ct);
        await audit.RecordAsync("user.invited", "Invite", invite.Id, $"Invited {invite.Email} as {invite.Role}.", ct);
        return Created($"/api/v1/team/invites/{invite.Id}", ToResponse(invite));
    }

    /// <summary>Accept an invitation (anonymous) — sets a password and creates the staff account.</summary>
    [HttpPost("invites/accept")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(AcceptInviteRequest request, CancellationToken ct)
    {
        await invites.AcceptAsync(request.Token, request.Password, ct);
        return NoContent();
    }

    private static InviteResponse ToResponse(Invite i) => new(
        i.Id, i.Email, i.Role.ToString(), i.ExpiresAtUtc, i.AcceptedAtUtc, i.InvitedByEmail);
}
