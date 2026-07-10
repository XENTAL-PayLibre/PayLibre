using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Parents;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Parent mobile app. Sign in for a bearer token, then view your children, their fee account details,
/// pending fees, and payment history. A parent sees only children whose guardian email matches their
/// account email. Fees are paid by bank transfer into the child's account — reconciliation is
/// automatic (Xental), so there is no charge endpoint here. All money in kobo.
/// </summary>
[ApiController]
[Route("api/v1/parent")]
public sealed class ParentController(ParentAuthService auth, ParentService parents) : ControllerBase
{
    /// <summary>Create a parent account. Returns a bearer access token.</summary>
    [HttpPost("auth/register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ParentSessionResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ParentSessionResponse>> Register(ParentRegisterRequest request, CancellationToken ct)
    {
        var s = await auth.RegisterAsync(request.Email, request.Password, request.FullName, request.Phone, ct);
        return Created($"/api/v1/parent", ToSession(s));
    }

    /// <summary>Step 1 of sign-in: verify the password and email a one-time code. Returns 202.</summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(LoginChallengeResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginChallengeResponse>> Login(ParentLoginRequest request, CancellationToken ct)
    {
        var challenge = await auth.BeginLoginAsync(request.Email, request.Password, ct);
        return Accepted(new LoginChallengeResponse(challenge.Email, challenge.ExpiresAtUtc,
            "A sign-in code was sent to your email. Enter it to finish signing in."));
    }

    /// <summary>Step 2 of sign-in: verify the emailed code. Returns a bearer access token.</summary>
    [HttpPost("auth/login/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ParentSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ParentSessionResponse>> VerifyLogin(VerifyOtpRequest request, CancellationToken ct)
    {
        var s = await auth.VerifyLoginOtpAsync(request.Email, request.Code, ct);
        return Ok(ToSession(s));
    }

    /// <summary>The parent's children, each with its fee account + total outstanding.</summary>
    [HttpGet("students")]
    [Authorize(Policy = AuthPolicies.Parent)]
    [ProducesResponseType(typeof(IEnumerable<ParentChildResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ParentChildResponse>>> Children(CancellationToken ct) =>
        Ok((await parents.GetChildrenAsync(Email(), ct)).Select(c => new ParentChildResponse(
            c.StudentId, c.FullName, c.AdmissionNo, c.SchoolName, c.ClassName, c.Nuban, c.BankName, c.AccountName, c.OutstandingKobo)));

    /// <summary>A child's fee invoices. <c>openOnly=true</c> (default) returns just the unpaid ones.</summary>
    [HttpGet("students/{studentId:guid}/fees")]
    [Authorize(Policy = AuthPolicies.Parent)]
    [ProducesResponseType(typeof(IEnumerable<ParentFeeResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ParentFeeResponse>>> Fees(Guid studentId, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        Ok((await parents.GetChildFeesAsync(Email(), studentId, openOnly, ct)).Select(f => new ParentFeeResponse(
            f.StudentFeeId, f.FeeName, f.AmountKobo, f.AmountPaidKobo, f.OutstandingKobo, f.Status, f.DueDateUtc)));

    /// <summary>The account to transfer to for a child + the total outstanding.</summary>
    [HttpGet("students/{studentId:guid}/payment-details")]
    [Authorize(Policy = AuthPolicies.Parent)]
    [ProducesResponseType(typeof(ParentPaymentDetailsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParentPaymentDetailsResponse>> PaymentDetails(Guid studentId, CancellationToken ct)
    {
        var d = await parents.GetPaymentDetailsAsync(Email(), studentId, ct);
        return Ok(new ParentPaymentDetailsResponse(d.StudentName, d.Nuban, d.BankName, d.AccountName, d.OutstandingKobo));
    }

    /// <summary>Payment history across the parent's children.</summary>
    [HttpGet("payments")]
    [Authorize(Policy = AuthPolicies.Parent)]
    [ProducesResponseType(typeof(IEnumerable<ParentPaymentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ParentPaymentResponse>>> Payments(CancellationToken ct) =>
        Ok((await parents.GetPaymentsAsync(Email(), ct)).Select(p => new ParentPaymentResponse(p.Id, p.StudentName, p.AmountKobo, p.OccurredAtUtc)));

    private string Email() =>
        User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value
        ?? throw new AuthenticationException("No parent identity on the request.");

    private static ParentSessionResponse ToSession(ParentSession s) => new(
        s.Parent.Id, s.Parent.Email, s.Access.Token, "Bearer",
        (int)Math.Max(0, (s.Access.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
}
