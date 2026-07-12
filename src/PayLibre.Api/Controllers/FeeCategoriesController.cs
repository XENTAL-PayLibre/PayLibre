using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Fees;
using PayLibre.Domain.Fees;

namespace PayLibre.Api.Controllers;

/// <summary>Fee categories (Tuition, PTA, Uniform, …) used to group fees.</summary>
[ApiController]
[Route("api/v1/fee-categories")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class FeeCategoriesController(FeeCategoryService categories) : ControllerBase
{
    /// <summary>List the school's fee categories.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FeeCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<FeeCategoryResponse>>> List(CancellationToken ct) =>
        Ok((await categories.ListAsync(ct)).Select(ToResponse));

    /// <summary>Create a fee category.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(typeof(FeeCategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeeCategoryResponse>> Create(FeeCategoryRequest request, CancellationToken ct)
    {
        var c = await categories.CreateAsync(new FeeCategoryInput(request.Name), ct);
        return Created($"/api/v1/fee-categories/{c.Id}", ToResponse(c));
    }

    /// <summary>Rename a fee category.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(typeof(FeeCategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FeeCategoryResponse>> Update(Guid id, FeeCategoryRequest request, CancellationToken ct) =>
        Ok(ToResponse(await categories.UpdateAsync(id, new FeeCategoryInput(request.Name), ct)));

    /// <summary>Delete a fee category (only if no fees use it).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }

    private static FeeCategoryResponse ToResponse(FeeCategory c) => new(c.Id, c.Name);
}
