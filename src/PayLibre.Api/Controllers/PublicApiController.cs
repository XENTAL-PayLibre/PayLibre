using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLibre.Api.Auth;
using PayLibre.Api.Contracts;
using PayLibre.Application.Enrolment;
using PayLibre.Domain.Enrolment;

namespace PayLibre.Api.Controllers;

/// <summary>
/// PayLibre's public API for a school's own systems (SIS, website). Authenticated with an
/// <c>X-Api-Key</c> header (or <c>Authorization: Bearer plb_…</c>) — see the API-keys page. Sync
/// students and read balances; each endpoint requires the matching key scope. All money in kobo.
/// </summary>
[ApiController]
[Route("api/v1/public")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("webhook")]
public sealed class PublicApiController(StudentService students) : ControllerBase
{
    /// <summary>List students in the school (scope: students:read).</summary>
    [HttpGet("students")]
    [Authorize(Policy = "api:students:read")]
    [ProducesResponseType(typeof(IEnumerable<PublicStudentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PublicStudentResponse>>> ListStudents(CancellationToken ct)
    {
        var list = await students.ListAsync(null, null, ct);
        var result = new List<PublicStudentResponse>(list.Count);
        foreach (var s in list)
            result.Add(new PublicStudentResponse(s.AdmissionNo, s.FullName, s.Status.ToString(), 0, s.Nuban, s.BankName, s.AccountName));
        return Ok(result);
    }

    /// <summary>Create or update a student by admission number — idempotent sync (scope: students:write).
    /// New students are auto-provisioned a virtual account.</summary>
    [HttpPost("students")]
    [Authorize(Policy = "api:students:write")]
    [ProducesResponseType(typeof(PublicStudentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicStudentResponse>> UpsertStudent(PublicUpsertStudentRequest request, CancellationToken ct)
    {
        var (student, created) = await students.UpsertByAdmissionNoAsync(new StudentInput(
            request.AdmissionNo, request.FullName, request.ClassId, request.Session,
            request.GuardianName, request.GuardianPhone, request.GuardianEmail), ct);
        var body = new PublicStudentResponse(student.AdmissionNo, student.FullName, student.Status.ToString(), 0,
            student.Nuban, student.BankName, student.AccountName);
        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    /// <summary>A student's account details + total outstanding balance (scope: students:read).</summary>
    [HttpGet("students/{admissionNo}")]
    [Authorize(Policy = "api:students:read")]
    [ProducesResponseType(typeof(PublicStudentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicStudentResponse>> GetStudent(string admissionNo, CancellationToken ct)
    {
        var (s, outstanding) = await students.GetWithOutstandingAsync(admissionNo, ct);
        return Ok(new PublicStudentResponse(s.AdmissionNo, s.FullName, s.Status.ToString(), outstanding, s.Nuban, s.BankName, s.AccountName));
    }
}
