using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Parents;

namespace PayLibre.Application.Parents;

public sealed record ParentSession(Parent Parent, AccessToken Access);

/// <summary>
/// Parent-app authentication. Registration is one step; sign-in is two steps (password → emailed
/// code → bearer token) — same as the school dashboard.
/// </summary>
public sealed class ParentAuthService(IApplicationDbContext db, IPasswordHasher hasher, ITokenService tokens, INotificationSender notifier, IClock clock)
{
    public async Task<ParentSession> RegisterAsync(string email, string password, string? fullName, string? phone, CancellationToken ct = default)
    {
        email = Norm(email);
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("An email is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
        if (await db.Parents.AnyAsync(p => p.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var parent = new Parent(email, hasher.Hash(password), fullName, phone);
        db.Parents.Add(parent);
        await db.SaveChangesAsync(ct);
        try { await notifier.SendWelcomeAsync(parent.Email, parent.FullName ?? "there", ct); } catch { /* best-effort */ }
        return new ParentSession(parent, tokens.IssueParentToken(parent));
    }

    /// <summary>Step 1: verify the password and email a one-time code.</summary>
    public async Task<LoginChallenge> BeginLoginAsync(string email, string password, CancellationToken ct = default)
    {
        email = Norm(email);
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Email == email, ct);
        if (parent is null || !hasher.Verify(password ?? string.Empty, parent.PasswordHash))
            throw new AuthenticationException("Invalid email or password.");

        var code = AuthService.GenerateOtp();
        var otp = new LoginOtp(OtpSubject.Parent, parent.Id, email, Sha256(code), clock.UtcNow.AddMinutes(10));
        db.LoginOtps.Add(otp);
        await db.SaveChangesAsync(ct);
        try { await notifier.SendLoginCodeAsync(email, code, ct); } catch { /* best-effort */ }
        return new LoginChallenge(email, otp.ExpiresAtUtc);
    }

    /// <summary>Step 2: verify the emailed code and return a bearer token.</summary>
    public async Task<ParentSession> VerifyLoginOtpAsync(string email, string code, CancellationToken ct = default)
    {
        email = Norm(email);
        var otp = (await db.LoginOtps.IgnoreQueryFilters()
                .Where(o => o.Subject == OtpSubject.Parent && o.Email == email && !o.Consumed).ToListAsync(ct))
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
        var parent = await db.Parents.FirstAsync(p => p.Id == otp.SubjectId, ct);
        await db.SaveChangesAsync(ct);
        return new ParentSession(parent, tokens.IssueParentToken(parent));
    }

    private static string Norm(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
