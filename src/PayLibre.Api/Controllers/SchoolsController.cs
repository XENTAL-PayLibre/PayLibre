using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Schools;
using PayLibre.Domain.Schools;

namespace PayLibre.Api.Controllers;

/// <summary>
/// The current school's profile + settlement (payout) account. Settlement details are configured here
/// — from inside the app — rather than at registration, and can be changed at any time. Setting them
/// pushes the payout account to the school's Xental sub-merchant (Xental resolves the account name).
/// </summary>
[ApiController]
[Route("api/v1/schools")]
public sealed class SchoolsController(SchoolService schools, AuditService audit) : ControllerBase
{
    /// <summary>The current school (includes whether a payout account has been configured).</summary>
    [HttpGet("current")]
    [Authorize(Policy = AuthPolicies.StaffRead)] // exposes payout account — not for ClassTeacher
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SchoolResponse>> Current(CancellationToken ct) =>
        Ok(ToSchool(await schools.GetCurrentAsync(ct)));

    /// <summary>Settlement position at the payment provider — collected / settled / pending (net kobo) —
    /// plus the configured payout account.</summary>
    [HttpGet("settlement-report")]
    [Authorize(Policy = AuthPolicies.StaffRead)] // payout bank + revenue — not for ClassTeacher
    [ProducesResponseType(typeof(SettlementReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettlementReportResponse>> SettlementReport(CancellationToken ct)
    {
        var r = await schools.GetSettlementReportAsync(ct);
        return Ok(new SettlementReportResponse(
            r.Configured, r.CollectedKobo, r.SettledKobo, r.PendingKobo, r.VirtualAccounts,
            r.BankName, r.AccountNumber, r.AccountName));
    }

    /// <summary>Set (or change) the school's payout account. Owner/Admin only. <c>bankName</c>/<c>bankCode</c>
    /// come from <c>GET /api/v1/banks</c>; the account name is resolved by the payment provider.</summary>
    [HttpPut("settlement")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SchoolResponse>> UpdateSettlement(UpdateSettlementRequest request, CancellationToken ct)
    {
        var school = await schools.UpdateSettlementAsync(request.BankName, request.BankCode, request.AccountNumber, ct);
        await audit.RecordAsync("settlement.updated", "School", school.Id,
            $"Payout account set to {school.SettlementBankName} {school.SettlementAccountNumber} ({school.SettlementAccountName}).", ct);
        return Ok(ToSchool(school));
    }

    /// <summary>Set the school's late-fee policy (Owner/Admin). A percentage of the outstanding balance,
    /// applied once a fee is overdue past the grace period. <c>lateFeeBps = 0</c> disables late fees.</summary>
    [HttpPut("late-fees")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SchoolResponse>> UpdateLateFees(UpdateLateFeesRequest request, CancellationToken ct)
    {
        var school = await schools.UpdateLateFeesAsync(request.LateFeeBps, request.GraceDays, ct);
        await audit.RecordAsync("school.late_fees_updated", "School", school.Id,
            $"Late fee set to {school.LateFeeBps} bps with a {school.LateFeeGraceDays}-day grace period.", ct);
        return Ok(ToSchool(school));
    }

    /// <summary>Replace a class teacher's assigned classes (Owner/Admin). Effective on their next sign-in.</summary>
    [HttpPut("users/{userId:guid}/classes")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUserClasses(Guid userId, SetUserClassesRequest request, CancellationToken ct)
    {
        var count = await schools.SetUserClassesAsync(userId, request.ClassIds, ct);
        await audit.RecordAsync("user.classes_updated", "SchoolUser", userId, $"Set {count} class assignment(s).", ct);
        return Ok(new { assigned = count });
    }

    private static SchoolResponse ToSchool(School s) => new(
        s.Id, s.Name, s.OfficialEmail, s.Phone, s.Status.ToString(),
        s.SettlementBankName, s.SettlementAccountNumber, s.SettlementAccountName, s.SettlementConfigured,
        s.LateFeeBps, s.LateFeeGraceDays, s.JoinCode);
}
