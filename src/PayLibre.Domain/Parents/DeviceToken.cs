using PayLibre.Domain.Common;

namespace PayLibre.Domain.Parents;

/// <summary>
/// A push-notification device token registered by a parent (their app install). Global (not tenant-owned),
/// keyed by the parent's email — the same identity used to match children. Used to deliver payment
/// notifications. One row per token.
/// </summary>
public sealed class DeviceToken : BaseEntity
{
    public string ParentEmail { get; private set; } = null!;
    public string Token { get; private set; } = null!;
    public string Platform { get; private set; } = null!;   // ios | android | web

    private DeviceToken() { }

    public DeviceToken(string parentEmail, string token, string platform)
    {
        ParentEmail = DomainException.Require(parentEmail, nameof(parentEmail)).Trim().ToLowerInvariant();
        Token = DomainException.Require(token, nameof(token)).Trim();
        Platform = string.IsNullOrWhiteSpace(platform) ? "unknown" : platform.Trim().ToLowerInvariant();
    }
}
