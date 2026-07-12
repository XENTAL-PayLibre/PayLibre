using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.ApiKeys;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.KeyPrefix).HasMaxLength(24).IsRequired();
        b.Property(x => x.KeyHash).HasMaxLength(100).IsRequired();
        b.Property(x => x.Scopes).HasMaxLength(400).IsRequired();
        b.HasIndex(x => x.KeyPrefix).IsUnique();     // lookup on authentication
        b.HasIndex(x => x.SchoolId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}
