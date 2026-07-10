using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations` build the context without the full app host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayLibreDbContext>
{
    public PayLibreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PayLibreDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=paylibre;Username=paylibre;Password=paylibre")
            .Options;
        return new PayLibreDbContext(options, new NoTenant(), new NoClock());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid RequireTenantId() => throw new InvalidOperationException();
    }

    private sealed class NoClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
