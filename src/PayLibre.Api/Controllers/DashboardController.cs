using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Application.Dashboard;

namespace PayLibre.Api.Controllers;

/// <summary>Dashboard overview + collection reports for the school (all money in kobo).</summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = AuthPolicies.Dashboard)]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Headline metrics: students, invoiced/collected/outstanding, collection rate,
    /// paid/overdue counts, a monthly revenue series, and recent payments.</summary>
    [HttpGet("dashboard/overview")]
    [ProducesResponseType(typeof(DashboardOverview), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardOverview>> Overview(CancellationToken ct) =>
        Ok(await dashboard.GetOverviewAsync(ct));

    /// <summary>Students with an outstanding balance, largest first.</summary>
    [HttpGet("reports/outstanding")]
    [ProducesResponseType(typeof(IEnumerable<OutstandingRow>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OutstandingRow>>> Outstanding(CancellationToken ct) =>
        Ok(await dashboard.OutstandingAsync(ct));

    /// <summary>Invoiced / collected / outstanding rolled up per class.</summary>
    [HttpGet("reports/collections")]
    [ProducesResponseType(typeof(IEnumerable<ClassCollection>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ClassCollection>>> Collections(CancellationToken ct) =>
        Ok(await dashboard.CollectionsByClassAsync(ct));
}
