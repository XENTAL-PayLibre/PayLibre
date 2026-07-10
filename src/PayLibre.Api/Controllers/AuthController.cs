using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLibre.Api.Auth;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Schools;
using PayLibre.Domain.Schools;

namespace PayLibre.Api.Controllers;

/// <summary>School registration + dashboard session auth. Tokens live only in HttpOnly cookies.</summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(AuthService auth, SchoolService schools, AuthCookieWriter cookies) : ControllerBase
{
    /// <summary>Register a school (creates the owner + provisions the school's Xental settlement).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<SchoolResponse>> Register(RegisterSchoolRequest request, CancellationToken ct)
    {
        var session = await auth.RegisterAsync(new RegisterSchoolInput(
            request.SchoolName, request.OfficialEmail, request.Phone,
            request.SettlementBankName, request.SettlementBankCode, request.SettlementAccountNumber, request.Password), ct);
        cookies.SetSession(Response, session);
        return Created($"/api/v1/schools/{session.School.Id}", ToSchool(session.School));
    }

    /// <summary>Sign in; sets the session cookies.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(SchoolResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SchoolResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var session = await auth.LoginAsync(request.Email, request.Password, ct);
        cookies.SetSession(Response, session);
        return Ok(ToSchool(session.School));
    }

    /// <summary>Rotate the session from the refresh cookie.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieWriter.RefreshCookie, out var refresh);
        var session = await auth.RefreshAsync(refresh ?? string.Empty, ct);
        cookies.SetSession(Response, session);
        return NoContent();
    }

    /// <summary>Sign out; revokes the refresh token and clears the cookies.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieWriter.RefreshCookie, out var refresh);
        await auth.LogoutAsync(refresh, ct);
        cookies.Clear(Response);
        return NoContent();
    }

    /// <summary>The current authenticated user + school.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Dashboard)]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value ?? throw new AuthenticationException("No user."));
        var user = await schools.GetUserAsync(userId, ct);
        var school = await schools.GetCurrentAsync(ct);
        return Ok(new MeResponse(user.Id, user.Email, user.Role.ToString(), ToSchool(school)));
    }

    private static SchoolResponse ToSchool(School s) => new(
        s.Id, s.Name, s.OfficialEmail, s.Phone, s.Status.ToString(),
        s.SettlementBankName, s.SettlementAccountNumber, s.SettlementAccountName);
}
