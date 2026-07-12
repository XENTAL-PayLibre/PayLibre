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

    /// <summary>Write access to day-to-day data (fees, students, classes): Owner/Admin/Bursar.
    /// Excludes read-only roles (Accountant, Auditor).</summary>
    public const string StaffWrite = "staff-write";

    /// <summary>View the audit trail: Owner/Admin/Auditor.</summary>
    public const string ViewAudit = "view-audit";

    /// <summary>Parent-app account (mobile).</summary>
    public const string Parent = "parent";
}
