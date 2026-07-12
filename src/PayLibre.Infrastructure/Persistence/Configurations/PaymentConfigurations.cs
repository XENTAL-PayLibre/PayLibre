using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.Fees;
using PayLibre.Domain.Payments;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.XentalTransactionRef).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.XentalTransactionRef).IsUnique(); // global idempotency on the provider ref
        b.Property(x => x.AmountKobo).IsRequired();
        b.Property(x => x.NetCreditKobo).IsRequired();
        b.Property(x => x.PayerName).HasMaxLength(200);
        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.StudentId });
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events");                 // platform-global (no tenant filter)
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(80);
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.ReceivedAtUtc).IsRequired();
        b.HasIndex(x => x.Reference);
        b.HasIndex(x => x.Status);                    // operators query the dead-letter (Status = Failed)
    }
}

public sealed class FeeAllocationConfiguration : IEntityTypeConfiguration<FeeAllocation>
{
    public void Configure(EntityTypeBuilder<FeeAllocation> b)
    {
        b.ToTable("fee_allocations");
        b.HasKey(x => x.Id);
        b.Property(x => x.AmountKobo).IsRequired();
        b.HasIndex(x => x.PaymentId);
        b.HasIndex(x => x.StudentFeeId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<StudentFee>().WithMany().HasForeignKey(x => x.StudentFeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
