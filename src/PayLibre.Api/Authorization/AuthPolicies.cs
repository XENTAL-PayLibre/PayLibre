namespace PayLibre.Api.Authorization;

/// <summary>Authorization policy + claim names for the dashboard plane.</summary>
public static class AuthPolicies
{
    public const string ScopeClaim = "scope";
    public const string RoleClaim = "role";

    /// <summary>Any authenticated school dashboard user.</summary>
    public const string Dashboard = "dashboard";

    /// <summary>Owner/Admin only (settlement + destructive settings).</summary>
    public const string ManageSchool = "manage-school";
}
