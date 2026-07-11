using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLibre.Api.Contracts;
using PayLibre.Application.Enrolment;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Public parent self-enrolment via a school's join code — no login. The school shares its code
/// (from its profile); a parent looks it up and adds their child, which provisions a dedicated
/// virtual account immediately. Rate-limited.
/// </summary>
[ApiController]
[Route("api/v1/enrol")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class EnrolmentController(SelfEnrolmentService enrolment) : ControllerBase
{
    /// <summary>Resolve a join code to the school name + its classes (to populate the enrol form).</summary>
    [HttpGet("{joinCode}")]
    [ProducesResponseType(typeof(EnrolContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnrolContextResponse>> Context(string joinCode, CancellationToken ct)
    {
        var c = await enrolment.GetContextAsync(joinCode, ct);
        return Ok(new EnrolContextResponse(c.SchoolName,
            c.Classes.Select(x => new EnrolContextClassResponse(x.Id, x.Name, x.Session)).ToList()));
    }

    /// <summary>Enrol a child under the school's join code. Returns the student's virtual-account details.</summary>
    [HttpPost("{joinCode}")]
    [ProducesResponseType(typeof(VirtualAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VirtualAccountResponse>> Enrol(string joinCode, SelfEnrolRequest request, CancellationToken ct)
    {
        var s = await enrolment.EnrolAsync(joinCode,
            new SelfEnrolInput(request.FullName, request.ClassId, request.GuardianName, request.GuardianPhone, request.GuardianEmail), ct);
        return Created($"/api/v1/students/{s.Id}", new VirtualAccountResponse(s.Nuban!, s.BankName!, s.AccountName!));
    }
}
