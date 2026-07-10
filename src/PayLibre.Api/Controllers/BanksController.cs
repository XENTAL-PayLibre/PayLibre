using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Contracts;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Api.Controllers;

/// <summary>Payout banks (name + code) for the settlement-bank dropdown at registration. Sourced from Xental.</summary>
[ApiController]
[Route("api/v1/banks")]
[AllowAnonymous]
public sealed class BanksController(IXentalClient xental) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BankResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BankResponse>>> List(CancellationToken ct) =>
        Ok((await xental.ListBanksAsync(ct)).Select(b => new BankResponse(b.Name, b.Code)));
}
