using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.Schools;
using PayLibre.Domain.Webhooks;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable("webhook_subscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Url).HasMaxLength(500).IsRequired();
        b.Property(x => x.SigningSecret).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.SchoolId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("webhook_deliveries");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });   // worker scans pending + due
        b.HasIndex(x => x.SubscriptionId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WebhookSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
