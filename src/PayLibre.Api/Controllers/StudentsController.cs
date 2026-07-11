using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Domain.Enrolment;

namespace PayLibre.Api.Controllers;

[ApiController]
[Route("api/v1/students")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class StudentsController(StudentService students) : ControllerBase
{
    /// <summary>Student directory. Optional filters: classId, status (Active/Inactive).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StudentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StudentResponse>>> List(
        [FromQuery] Guid? classId, [FromQuery] StudentStatus? status, CancellationToken ct) =>
        Ok((await students.ListAsync(classId, status, ct)).Select(ToResponse));

    /// <summary>Get a student by id, including cached virtual-account details.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StudentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(ToResponse(await students.GetAsync(id, ct)));

    /// <summary>Create a student — provisions a dedicated virtual account automatically.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(StudentResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<StudentResponse>> Create(CreateStudentRequest request, CancellationToken ct)
    {
        var s = await students.CreateAsync(new StudentInput(
            request.AdmissionNo, request.FullName, request.ClassId, request.Session,
            request.GuardianName, request.GuardianPhone, request.GuardianEmail), ct);
        return Created($"/api/v1/students/{s.Id}", ToResponse(s));
    }

    /// <summary>Update a student's details (name, class, guardian). Does not re-provision the account.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StudentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StudentResponse>> Update(Guid id, CreateStudentRequest request, CancellationToken ct) =>
        Ok(ToResponse(await students.UpdateAsync(id, new StudentInput(
            request.AdmissionNo, request.FullName, request.ClassId, request.Session,
            request.GuardianName, request.GuardianPhone, request.GuardianEmail), ct)));

    /// <summary>Delete a student.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await students.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>The student's dedicated virtual account (NUBAN) card.</summary>
    [HttpGet("{id:guid}/virtual-account")]
    [ProducesResponseType(typeof(VirtualAccountResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<VirtualAccountResponse>> VirtualAccount(Guid id, CancellationToken ct)
    {
        var s = await students.GetAsync(id, ct);
        if (!s.HasVirtualAccount) return NotFound(new { error = "No virtual account for this student yet." });
        return Ok(new VirtualAccountResponse(s.Nuban!, s.BankName!, s.AccountName!));
    }

    /// <summary>Send the account details to the guardian (SMS/email).</summary>
    [HttpPost("{id:guid}/virtual-account/send")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendDetails(Guid id, CancellationToken ct)
    {
        await students.SendAccountDetailsAsync(id, ct);
        return NoContent();
    }

    /// <summary>Bulk-create students from a CSV file (multipart form field "file").
    /// Header: AdmissionNo,FullName,Class,Session,GuardianName,GuardianPhone,GuardianEmail.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(5_000_000)]
    [ProducesResponseType(typeof(ImportResultResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportResultResponse>> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("A non-empty CSV file is required.");
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(ct);
        var result = await students.ImportCsvAsync(csv, ct);
        return Ok(new ImportResultResponse(result.Created, result.Failed,
            result.Errors.Select(e => new ImportErrorResponse(e.Row, e.Message)).ToList()));
    }

    private static StudentResponse ToResponse(Student s) => new(
        s.Id, s.AdmissionNo, s.FullName, s.ClassId, s.Session,
        s.GuardianName, s.GuardianPhone, s.GuardianEmail, s.Status.ToString(),
        s.HasVirtualAccount, s.Nuban, s.BankName, s.AccountName);
}
