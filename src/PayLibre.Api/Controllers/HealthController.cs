using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayLibre.Infrastructure.Persistence;

namespace PayLibre.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(PayLibreDbContext db) : ControllerBase
{
    /// <summary>Lightweight liveness probe for the API.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        status = "Healthy",
        service = "PayLibre.Api",
        timestamp = DateTimeOffset.UtcNow,
    });

    /// <summary>Readiness probe: also verifies database connectivity. 503 when the DB is unreachable.</summary>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        bool dbOk;
        try { dbOk = await db.Database.CanConnectAsync(ct); }
        catch { dbOk = false; }
        var body = new { status = dbOk ? "Ready" : "NotReady", database = dbOk, timestamp = DateTimeOffset.UtcNow };
        return dbOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
