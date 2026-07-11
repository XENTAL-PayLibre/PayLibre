using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
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
public sealed class SchoolsController(SchoolService schools) : ControllerBase
{
    /// <summary>The current school (includes whether a payout account has been configured).</summary>
    [HttpGet("current")]
    [Authorize(Policy = AuthPolicies.Dashboard)]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SchoolResponse>> Current(CancellationToken ct) =>
        Ok(ToSchool(await schools.GetCurrentAsync(ct)));

    /// <summary>Set (or change) the school's payout account. Owner/Admin only. <c>bankName</c>/<c>bankCode</c>
    /// come from <c>GET /api/v1/banks</c>; the account name is resolved by the payment provider.</summary>
    [HttpPut("settlement")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SchoolResponse>> UpdateSettlement(UpdateSettlementRequest request, CancellationToken ct)
    {
        var school = await schools.UpdateSettlementAsync(request.BankName, request.BankCode, request.AccountNumber, ct);
        return Ok(ToSchool(school));
    }

    private static SchoolResponse ToSchool(School s) => new(
        s.Id, s.Name, s.OfficialEmail, s.Phone, s.Status.ToString(),
        s.SettlementBankName, s.SettlementAccountNumber, s.SettlementAccountName, s.SettlementConfigured, s.JoinCode);
}
