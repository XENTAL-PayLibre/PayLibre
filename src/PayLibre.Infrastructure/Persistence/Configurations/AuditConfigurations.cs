using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.Audit;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorEmail).HasMaxLength(200);
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(80);
        b.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.CreatedAtUtc });
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}
