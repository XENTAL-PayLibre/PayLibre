using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Parents;

namespace PayLibre.Application.Parents;

/// <summary>
/// Device-token registration + push delivery for the parent app. Tokens are keyed by parent email.
/// Delivery is best-effort and config-gated (see <see cref="IPushSender"/>): a no-op until push
/// credentials are configured.
/// </summary>
public sealed class PushService(IApplicationDbContext db, IPushSender sender)
{
    public async Task RegisterDeviceAsync(string parentEmail, string token, string platform, CancellationToken ct = default)
    {
        var email = (parentEmail ?? string.Empty).Trim().ToLowerInvariant();
        token = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token)) throw new ValidationException("A device token is required.");
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is not null) return; // idempotent — already registered
        db.DeviceTokens.Add(new DeviceToken(email, token, platform));
        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(string parentEmail, string token, CancellationToken ct = default)
    {
        var email = (parentEmail ?? string.Empty).Trim().ToLowerInvariant();
        token = (token ?? string.Empty).Trim();
        var rows = await db.DeviceTokens.Where(d => d.Token == token && d.ParentEmail == email).ToListAsync(ct);
        if (rows.Count > 0) { db.DeviceTokens.RemoveRange(rows); await db.SaveChangesAsync(ct); }
    }

    /// <summary>Best-effort push to every device of the given guardian emails.</summary>
    public async Task NotifyAsync(IReadOnlyList<string> guardianEmails, string title, string body, CancellationToken ct = default)
    {
        var emails = guardianEmails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        if (emails.Count == 0) return;
        var tokens = await db.DeviceTokens.Where(d => emails.Contains(d.ParentEmail)).Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0) return;
        await sender.SendAsync(tokens, title, body, ct);
    }
}
