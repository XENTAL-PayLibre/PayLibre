using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Fees;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Fees. Creating a fee for a class fans out one invoice (StudentFee) per active student. List/detail
/// return live collection figures; parents' payments settle these invoices (Payments module). Kobo.
/// </summary>
[ApiController]
[Route("api/v1/fees")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class FeesController(FeeService fees, AuditService audit) : ControllerBase
{
    /// <summary>List fees with rolled-up invoiced/collected/outstanding figures.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FeeResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<FeeResponse>>> List(CancellationToken ct) =>
        Ok((await fees.ListAsync(ct)).Select(ToResponse));

    /// <summary>Headline totals across all fees (for the Fees page cards).</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(FeeSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FeeSummaryResponse>> Summary(CancellationToken ct)
    {
        var s = await fees.SummaryAsync(ct);
        return Ok(new FeeSummaryResponse(s.TotalInvoicedKobo, s.CollectedKobo, s.OutstandingKobo, s.Fees, s.StudentFees));
    }

    /// <summary>A fee plus its per-student invoice breakdown.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FeeDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FeeDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        var (fee, invoices) = await fees.GetAsync(id, ct);
        // Roll up totals for the header from the invoices.
        var invoiced = invoices.Sum(i => i.Invoice.AmountKobo);
        var collected = invoices.Sum(i => i.Invoice.AmountPaidKobo);
        var className = invoices.FirstOrDefault()?.ClassName ?? "—";
        var header = new FeeResponse(fee.Id, fee.Name, fee.FeeCategoryId, "—", fee.ClassId, className,
            fee.Session, fee.Term.ToString(), fee.AmountKobo, fee.DueDateUtc,
            invoices.Count, invoiced, collected, Math.Max(0, invoiced - collected), fee.AppliesLateFee);
        return Ok(new FeeDetailResponse(header, invoices.Select(ToInvoice).ToList()));
    }

    /// <summary>Create a fee (fans out invoices to the class's active students).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(typeof(FeeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FeeResponse>> Create(CreateFeeRequest request, CancellationToken ct)
    {
        var (fee, _) = await fees.CreateAsync(new FeeSpec(
            request.Name, request.FeeCategoryId, request.ClassId, request.Session, request.Term, request.AmountKobo, request.DueDateUtc), ct);
        await audit.RecordAsync("fee.created", "Fee", fee.Id,
            $"Created fee \"{fee.Name}\" ({fee.AmountKobo} kobo) due {fee.DueDateUtc:yyyy-MM-dd}.", ct);
        // Re-read via list to include category/class names + counts.
        var stats = (await fees.ListAsync(ct)).First(f => f.Fee.Id == fee.Id);
        return Created($"/api/v1/fees/{fee.Id}", ToResponse(stats));
    }

    /// <summary>Delete a fee and its invoices (only if none have payments).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.StaffWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await fees.DeleteAsync(id, ct);
        await audit.RecordAsync("fee.deleted", "Fee", id, $"Deleted fee {id}.", ct);
        return NoContent();
    }

    private static FeeResponse ToResponse(FeeStats s) => new(
        s.Fee.Id, s.Fee.Name, s.Fee.FeeCategoryId, s.CategoryName, s.Fee.ClassId, s.ClassName,
        s.Fee.Session, s.Fee.Term.ToString(), s.Fee.AmountKobo, s.Fee.DueDateUtc,
        s.Students, s.InvoicedKobo, s.CollectedKobo, s.OutstandingKobo, s.Fee.AppliesLateFee);

    private static StudentFeeResponse ToInvoice(StudentFeeRow r) => new(
        r.Invoice.Id, r.Invoice.StudentId, r.StudentName, r.AdmissionNo, r.ClassName,
        r.Invoice.AmountKobo, r.Invoice.AmountPaidKobo, r.Invoice.OutstandingKobo, r.Invoice.Status.ToString(), r.Invoice.DueDateUtc,
        r.Invoice.LateFeeAppliedKobo);
}
