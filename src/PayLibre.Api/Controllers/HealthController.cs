using Microsoft.AspNetCore.Mvc;

namespace PayLibre.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Lightweight liveness probe for the API.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        status = "Healthy",
        service = "PayLibre.Api",
        timestamp = DateTimeOffset.UtcNow
    });
}
