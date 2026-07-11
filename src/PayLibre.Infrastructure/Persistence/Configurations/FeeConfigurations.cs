using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Fees;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class FeeCategoryConfiguration : IEntityTypeConfiguration<FeeCategory>
{
    public void Configure(EntityTypeBuilder<FeeCategory> b)
    {
        b.ToTable("fee_categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FeeConfiguration : IEntityTypeConfiguration<Fee>
{
    public void Configure(EntityTypeBuilder<Fee> b)
    {
        b.ToTable("fees");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Session).HasMaxLength(40).IsRequired();
        b.Property(x => x.Term).HasConversion<string>().HasMaxLength(12).IsRequired();
        b.Property(x => x.AmountKobo).IsRequired();
        b.Property(x => x.DueDateUtc).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.ClassId });
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FeeCategory>().WithMany().HasForeignKey(x => x.FeeCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StudentFeeConfiguration : IEntityTypeConfiguration<StudentFee>
{
    public void Configure(EntityTypeBuilder<StudentFee> b)
    {
        b.ToTable("student_fees");
        b.HasKey(x => x.Id);
        b.Property(x => x.AmountKobo).IsRequired();
        b.Property(x => x.AmountPaidKobo).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12).IsRequired();
        b.Property(x => x.DueDateUtc).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.StudentId });
        b.HasIndex(x => new { x.FeeId });
        b.HasIndex(x => new { x.SchoolId, x.FeeId, x.StudentId }).IsUnique(); // one invoice per student per fee
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Fee>().WithMany().HasForeignKey(x => x.FeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
