using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Common;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence;

public sealed class PayLibreDbContext(
    DbContextOptions<PayLibreDbContext> options,
    ITenantContext tenantContext,
    IClock clock) : DbContext(options), IApplicationDbContext
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IClock _clock = clock;

    public DbSet<School> Schools => Set<School>();
    public DbSet<SchoolUser> SchoolUsers => Set<SchoolUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<LoginOtp> LoginOtps => Set<LoginOtp>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<PayLibre.Domain.Fees.FeeCategory> FeeCategories => Set<PayLibre.Domain.Fees.FeeCategory>();
    public DbSet<PayLibre.Domain.Fees.Fee> Fees => Set<PayLibre.Domain.Fees.Fee>();
    public DbSet<PayLibre.Domain.Fees.StudentFee> StudentFees => Set<PayLibre.Domain.Fees.StudentFee>();
    public DbSet<PayLibre.Domain.Payments.Payment> Payments => Set<PayLibre.Domain.Payments.Payment>();
    public DbSet<PayLibre.Domain.Payments.FeeAllocation> FeeAllocations => Set<PayLibre.Domain.Payments.FeeAllocation>();
    public DbSet<PayLibre.Domain.Parents.Parent> Parents => Set<PayLibre.Domain.Parents.Parent>();

    // Evaluated per query against the current request's tenant. Guid.Empty (no tenant) matches no
    // rows → deny by default. Registration/login bypass this with IgnoreQueryFilters().
    private Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayLibreDbContext).Assembly);

        // Row-level tenant isolation.
        modelBuilder.Entity<School>().HasQueryFilter(e => e.Id == CurrentTenantId);
        modelBuilder.Entity<SchoolUser>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<Class>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<Student>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PayLibre.Domain.Fees.FeeCategory>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PayLibre.Domain.Fees.Fee>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PayLibre.Domain.Fees.StudentFee>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PayLibre.Domain.Payments.Payment>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
        modelBuilder.Entity<PayLibre.Domain.Payments.FeeAllocation>().HasQueryFilter(e => e.SchoolId == CurrentTenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAtUtc = now;
            else if (entry.State == EntityState.Modified) entry.Entity.UpdatedAtUtc = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
