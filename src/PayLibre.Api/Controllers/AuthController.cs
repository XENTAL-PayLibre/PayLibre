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

/// <summary>
/// School registration + dashboard session auth. Every successful auth response sets the session as
/// HttpOnly cookies (browser clients) <b>and</b> returns a bearer access token in the body (mobile /
/// API clients) — send it as <c>Authorization: Bearer &lt;token&gt;</c>. Includes forgot/reset password.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(AuthService auth, SchoolService schools, AuthCookieWriter cookies) : ControllerBase
{
    /// <summary>Register a school (creates the owner + provisions the school's Xental settlement). Sets
    /// session cookies and returns an access token.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthSessionResponse>> Register(RegisterSchoolRequest request, CancellationToken ct)
    {
        var session = await auth.RegisterAsync(new RegisterSchoolInput(
            request.SchoolName, request.OfficialEmail, request.Phone,
            request.SettlementBankName, request.SettlementBankCode, request.SettlementAccountNumber, request.Password), ct);
        cookies.SetSession(Response, session);
        return Created($"/api/v1/schools/{session.School.Id}", ToAuth(session));
    }

    /// <summary>Step 1 of sign-in: verify the password and email a one-time code. No session yet —
    /// call <c>login/verify</c> with the code to finish. Returns 202.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginChallengeResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginChallengeResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var challenge = await auth.BeginLoginAsync(request.Email, request.Password, ct);
        return Accepted(new LoginChallengeResponse(challenge.Email, challenge.ExpiresAtUtc,
            "A sign-in code was sent to your email. Enter it to finish signing in."));
    }

    /// <summary>Step 2 of sign-in: verify the emailed code. Sets session cookies + returns a token.</summary>
    [HttpPost("login/verify")]
    [ProducesResponseType(typeof(AuthSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthSessionResponse>> VerifyLogin(VerifyOtpRequest request, CancellationToken ct)
    {
        var session = await auth.VerifyLoginOtpAsync(request.Email, request.Code, ct);
        cookies.SetSession(Response, session);
        return Ok(ToAuth(session));
    }

    /// <summary>Rotate the session from the refresh cookie. Sets fresh cookies and returns a new access token.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthSessionResponse>> Refresh(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieWriter.RefreshCookie, out var refresh);
        var session = await auth.RefreshAsync(refresh ?? string.Empty, ct);
        cookies.SetSession(Response, session);
        return Ok(ToAuth(session));
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

    /// <summary>Request a password-reset link. Always returns 202 (no account enumeration).</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request.Email, ct);
        return Accepted(new { message = "If an account exists for that email, a reset link has been sent." });
    }

    /// <summary>Set a new password using the token from the reset email. Invalidates existing sessions.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request.Token, request.NewPassword, ct);
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

    private static AuthSessionResponse ToAuth(IssuedSession s) => new(
        ToSchool(s.School), s.User.Id, s.User.Email, s.User.Role.ToString(),
        s.Access.Token, "Bearer", (int)Math.Max(0, (s.Access.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));

    private static SchoolResponse ToSchool(School s) => new(
        s.Id, s.Name, s.OfficialEmail, s.Phone, s.Status.ToString(),
        s.SettlementBankName, s.SettlementAccountNumber, s.SettlementAccountName, s.JoinCode);
}
