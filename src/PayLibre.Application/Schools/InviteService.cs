using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Schools;

/// <summary>
/// Staff invitations. An Owner/Admin invites someone by email + role; the raw token is emailed (only
/// its hash is stored). The invitee accepts anonymously with the token + a new password, which creates
/// their <see cref="SchoolUser"/>. Owners can't be invited (only the registrant is an Owner).
/// </summary>
public sealed class InviteService(
    IApplicationDbContext db, ITenantContext tenant, IPasswordHasher hasher,
    INotificationSender notifier, IClock clock, IOptions<PayLibreOptions> options)
{
    private readonly PayLibreOptions _options = options.Value;

    public async Task<Invite> CreateAsync(string email, SchoolRole role, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("An email is required.");
        if (role == SchoolRole.Owner) throw new ValidationException("An Owner cannot be invited.");

        if (await db.SchoolUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("A user with this email already exists.");
        // Materialize then compare the timestamp in memory (SQLite can't translate DateTimeOffset comparisons).
        var hasActiveInvite = (await db.Invites.Where(i => i.Email == email && i.AcceptedAtUtc == null).ToListAsync(ct))
            .Any(i => i.ExpiresAtUtc > clock.UtcNow);
        if (hasActiveInvite)
            throw new ConflictException("An active invitation for this email already exists.");

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invite = new Invite(schoolId, email, role, Sha256(raw), clock.UtcNow.AddHours(_options.InviteTtlHours), tenant.UserEmail);
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);

        var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct);
        var url = $"{_options.FrontendUrl.TrimEnd('/')}/accept-invite?token={raw}";
        try { await notifier.SendStaffInviteAsync(email, school?.Name ?? "your school", role.ToString(), url, ct); }
        catch { /* best-effort */ }
        return invite;
    }

    /// <summary>Anonymous: redeem an invite token to create the staff account. The invitee then signs in normally.</summary>
    public async Task<SchoolUser> AcceptAsync(string token, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");

        var hash = Sha256(token ?? string.Empty);
        var invite = (await db.Invites.IgnoreQueryFilters()
                .Where(i => i.TokenHash == hash && i.AcceptedAtUtc == null).ToListAsync(ct))
            .FirstOrDefault();
        if (invite is null || !invite.IsRedeemable(clock.UtcNow))
            throw new ValidationException("This invitation is invalid or has expired.");

        if (await db.SchoolUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == invite.Email, ct))
            throw new ConflictException("A user with this email already exists.");

        var user = new SchoolUser(invite.SchoolId, invite.Email, hasher.Hash(password), invite.Role);
        invite.Accept(clock.UtcNow);
        db.SchoolUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<IReadOnlyList<Invite>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return (await db.Invites.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(i => i.CreatedAtUtc).ToList();
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
