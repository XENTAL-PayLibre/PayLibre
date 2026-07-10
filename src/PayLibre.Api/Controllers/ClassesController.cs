using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Enrolment;
using PayLibre.Domain.Enrolment;

namespace PayLibre.Api.Controllers;

[ApiController]
[Route("api/v1/classes")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class ClassesController(ClassService classes) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ClassResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ClassResponse>>> List(CancellationToken ct) =>
        Ok((await classes.ListAsync(ct)).Select(ToResponse));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ClassResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClassResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(ToResponse(await classes.GetAsync(id, ct)));

    [HttpPost]
    [ProducesResponseType(typeof(ClassResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ClassResponse>> Create(ClassRequest request, CancellationToken ct)
    {
        var klass = await classes.CreateAsync(new ClassInput(request.Name, request.Session), ct);
        return Created($"/api/v1/classes/{klass.Id}", ToResponse(klass));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ClassResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassResponse>> Update(Guid id, ClassRequest request, CancellationToken ct) =>
        Ok(ToResponse(await classes.UpdateAsync(id, new ClassInput(request.Name, request.Session), ct)));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await classes.DeleteAsync(id, ct);
        return NoContent();
    }

    private static ClassResponse ToResponse(Class c) => new(c.Id, c.Name, c.Session);
}
