using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Payments;

namespace PayLibre.Api.Controllers;

/// <summary>Payments received into students' virtual accounts (reconciled by Xental).</summary>
[ApiController]
[Route("api/v1/payments")]
[Authorize(Policy = AuthPolicies.StaffRead)] // school-wide payments; ClassTeacher excluded
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>List received payments, most recent first. Optionally filter by student.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PaymentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PaymentResponse>>> List(
        [FromQuery] Guid? studentId, [FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok((await payments.ListAsync(take, studentId, ct)).Select(ToResponse));

    private static PaymentResponse ToResponse(PaymentRow r) => new(
        r.Payment.Id, r.Payment.StudentId, r.StudentName, r.AdmissionNo,
        r.Payment.AmountKobo, r.Payment.NetCreditKobo, r.Payment.PayerName, r.Payment.OccurredAtUtc);
}
