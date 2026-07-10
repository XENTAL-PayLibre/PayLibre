using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Parents;

namespace PayLibre.Application.Parents;

public sealed record ParentSession(Parent Parent, AccessToken Access);

/// <summary>Parent-app authentication. Returns a bearer access token (scope=parent) for the mobile app.</summary>
public sealed class ParentAuthService(IApplicationDbContext db, IPasswordHasher hasher, ITokenService tokens)
{
    public async Task<ParentSession> RegisterAsync(string email, string password, string? fullName, string? phone, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("An email is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
        if (await db.Parents.AnyAsync(p => p.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var parent = new Parent(email, hasher.Hash(password), fullName, phone);
        db.Parents.Add(parent);
        await db.SaveChangesAsync(ct);
        return new ParentSession(parent, tokens.IssueParentToken(parent));
    }

    public async Task<ParentSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Email == email, ct);
        if (parent is null || !hasher.Verify(password ?? string.Empty, parent.PasswordHash))
            throw new AuthenticationException("Invalid email or password.");
        return new ParentSession(parent, tokens.IssueParentToken(parent));
    }
}
