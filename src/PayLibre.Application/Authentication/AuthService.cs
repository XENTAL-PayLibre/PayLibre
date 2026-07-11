using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Authentication;

public sealed record RegisterSchoolInput(
    string SchoolName, string OfficialEmail, string Phone, string Password);

public sealed record IssuedSession(
    AccessToken Access, string RefreshToken, DateTimeOffset RefreshExpiresAt, SchoolUser User, School School);

public sealed record LoginChallenge(string Email, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// School registration + dashboard authentication. Registration also provisions the school's Xental
/// sub-merchant + payout account (so Xental settles fees to the school's bank). Sessions are issued
/// as an access token + a rotating, hashed refresh token — both delivered only as HttpOnly cookies.
/// </summary>
public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IXentalClient xental,
    INotificationSender notifier,
    IClock clock,
    IOptions<PayLibreOptions> options)
{
    private readonly PayLibreOptions _options = options.Value;

    public async Task<IssuedSession> RegisterAsync(RegisterSchoolInput input, CancellationToken ct = default)
    {
        var email = (input.OfficialEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new ValidationException("An official email is required.");
        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
        if (await db.SchoolUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        // Id is assigned at construction, so we can reference it with Xental before persisting —
        // if any Xental call fails, nothing is saved (clean rollback). The payout account is NOT set
        // here: the school configures where fees settle later, from inside the app (Settings).
        var school = new School(input.SchoolName, email, input.Phone);
        var owner = new SchoolUser(school.Id, email, hasher.Hash(input.Password), SchoolRole.Owner);

        var reference = $"sch_{school.Id:N}";
        XentalSubMerchant sub;
        try
        {
            sub = await xental.CreateSubMerchantAsync(school.Name, reference, ct);
        }
        catch (Exception ex) when (ex is not ValidationException and not ConflictException)
        {
            throw new UpstreamException($"Could not set up the school with the payment provider: {ex.Message}");
        }
        school.LinkXentalSubMerchant(sub.Reference, sub.Id, sub.SettlementAccountName);

        string joinCode;
        do { joinCode = GenerateJoinCode(); }
        while (await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.JoinCode == joinCode, ct));
        school.SetJoinCode(joinCode);

        db.Schools.Add(school);
        db.SchoolUsers.Add(owner);
        var session = await IssueSessionAsync(owner, school, ct);
        await db.SaveChangesAsync(ct);
        try { await notifier.SendWelcomeAsync(owner.Email, school.Name, ct); } catch { /* best-effort */ }
        return session;
    }

    /// <summary>Step 1 of login: verify the password and email a one-time code. No session yet.</summary>
    public async Task<LoginChallenge> BeginLoginAsync(string email, string password, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.SchoolUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !hasher.Verify(password ?? string.Empty, user.PasswordHash))
            throw new AuthenticationException("Invalid email or password.");

        var code = GenerateOtp();
        var otp = new LoginOtp(OtpSubject.SchoolUser, user.Id, email, Sha256(code), clock.UtcNow.AddMinutes(10));
        db.LoginOtps.Add(otp);
        await db.SaveChangesAsync(ct);
        try { await notifier.SendLoginCodeAsync(email, code, ct); } catch { /* best-effort */ }
        return new LoginChallenge(email, otp.ExpiresAtUtc);
    }

    /// <summary>Step 2 of login: verify the emailed code and start the session.</summary>
    public async Task<IssuedSession> VerifyLoginOtpAsync(string email, string code, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var otp = (await db.LoginOtps.IgnoreQueryFilters()
                .Where(o => o.Subject == OtpSubject.SchoolUser && o.Email == email && !o.Consumed).ToListAsync(ct))
            .OrderByDescending(o => o.CreatedAtUtc).FirstOrDefault();
        if (otp is null || !otp.IsRedeemable(clock.UtcNow))
            throw new AuthenticationException("Invalid or expired code. Please sign in again.");
        if (Sha256(code ?? string.Empty) != otp.CodeHash)
        {
            otp.RegisterFailedAttempt();
            await db.SaveChangesAsync(ct);
            throw new AuthenticationException("Invalid code.");
        }
        otp.Consume();
        var user = await db.SchoolUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == otp.SubjectId, ct);
        var school = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == user.SchoolId, ct);
        var session = await IssueSessionAsync(user, school, ct);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<IssuedSession> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw new AuthenticationException("Missing refresh token.");
        var hash = Sha256(rawRefreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || !token.IsActive(clock.UtcNow))
            throw new AuthenticationException("Session expired. Please sign in again.");

        token.Revoke(); // rotate
        var user = await db.SchoolUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == token.SchoolUserId, ct);
        var school = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == user.SchoolId, ct);
        var session = await IssueSessionAsync(user, school, ct);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var hash = Sha256(rawRefreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null) { token.Revoke(); await db.SaveChangesAsync(ct); }
    }

    /// <summary>Begin a password reset: email the user a single-use reset link. Always succeeds
    /// (no account enumeration) — callers should return 202 regardless.</summary>
    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.SchoolUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return; // silent — don't reveal whether the account exists

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        db.PasswordResetTokens.Add(new PasswordResetToken(
            user.SchoolId, user.Id, Sha256(raw), clock.UtcNow.AddMinutes(_options.PasswordResetTtlMinutes)));
        await db.SaveChangesAsync(ct);

        var url = $"{_options.FrontendUrl.TrimEnd('/')}/reset-password?token={raw}";
        try { await notifier.SendPasswordResetAsync(user.Email, url, ct); } catch { /* delivery is best-effort */ }
    }

    /// <summary>Complete a password reset with the emailed token. Consumes the token and revokes all
    /// of the user's existing sessions.</summary>
    public async Task ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
        var token = await db.PasswordResetTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == Sha256(rawToken ?? string.Empty), ct);
        if (token is null || !token.IsRedeemable(clock.UtcNow))
            throw new ValidationException("This reset link is invalid or has expired. Request a new one.");

        var user = await db.SchoolUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == token.SchoolUserId, ct);
        user.SetPasswordHash(hasher.Hash(newPassword));
        token.Consume();
        // Invalidate any active sessions for this user.
        var sessions = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(r => r.SchoolUserId == user.Id && !r.Revoked).ToListAsync(ct);
        foreach (var s in sessions) s.Revoke();
        await db.SaveChangesAsync(ct);
    }

    private async Task<IssuedSession> IssueSessionAsync(SchoolUser user, School school, CancellationToken ct)
    {
        var access = tokens.IssueAccessToken(user);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expires = clock.UtcNow.AddDays(_options.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken(school.Id, user.Id, Sha256(raw), expires));
        await Task.CompletedTask;
        return new IssuedSession(access, raw, expires, user, school);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // 6-digit numeric sign-in code.
    public static string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    // Unambiguous 8-char code (no 0/O/1/I) parents type to self-enrol.
    private static string GenerateJoinCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        return string.Concat(bytes.Select(b => alphabet[b % alphabet.Length]));
    }
}
