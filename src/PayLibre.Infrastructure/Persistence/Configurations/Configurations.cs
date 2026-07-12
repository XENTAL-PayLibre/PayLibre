using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Persistence.Configurations;

public sealed class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    public void Configure(EntityTypeBuilder<School> b)
    {
        b.ToTable("schools");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.OfficialEmail).HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.OfficialEmail).IsUnique();
        b.Property(x => x.Phone).HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.SettlementBankName).HasMaxLength(200);
        b.Property(x => x.SettlementBankCode).HasMaxLength(16);
        b.Property(x => x.SettlementAccountNumber).HasMaxLength(20);
        b.Property(x => x.SettlementAccountName).HasMaxLength(200);
        b.Property(x => x.XentalSubMerchantRef).HasMaxLength(100);
        b.Property(x => x.JoinCode).HasMaxLength(16);
        b.HasIndex(x => x.JoinCode).IsUnique();
    }
}

public sealed class SchoolUserConfiguration : IEntityTypeConfiguration<SchoolUser>
{
    public void Configure(EntityTypeBuilder<SchoolUser> b)
    {
        b.ToTable("school_users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.SchoolId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> b)
    {
        b.ToTable("invites");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(100).IsRequired();
        b.Property(x => x.InvitedByEmail).HasMaxLength(320);
        b.HasIndex(x => x.TokenHash);
        b.HasIndex(x => new { x.SchoolId, x.Email });
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ParentConfiguration : IEntityTypeConfiguration<PayLibre.Domain.Parents.Parent>
{
    public void Configure(EntityTypeBuilder<PayLibre.Domain.Parents.Parent> b)
    {
        b.ToTable("parents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.HasIndex(x => x.SchoolUserId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("password_reset_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.HasIndex(x => x.SchoolUserId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LoginOtpConfiguration : IEntityTypeConfiguration<LoginOtp>
{
    public void Configure(EntityTypeBuilder<LoginOtp> b)
    {
        b.ToTable("login_otps");
        b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.HasIndex(x => new { x.Subject, x.SubjectId, x.Consumed });
    }
}

public sealed class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> b)
    {
        b.ToTable("classes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Session).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.Name, x.Session }).IsUnique();
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("students");
        b.HasKey(x => x.Id);
        b.Property(x => x.AdmissionNo).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.AdmissionNo }).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Session).HasMaxLength(40).IsRequired();
        b.Property(x => x.GuardianName).HasMaxLength(200).IsRequired();
        b.Property(x => x.GuardianPhone).HasMaxLength(40);
        b.Property(x => x.GuardianEmail).HasMaxLength(320);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.XentalAccountRef).HasMaxLength(100);
        b.Property(x => x.Nuban).HasMaxLength(20);
        b.Property(x => x.BankName).HasMaxLength(200);
        b.Property(x => x.AccountName).HasMaxLength(200);
        b.HasIndex(x => x.ClassId);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> b)
    {
        b.ToTable("student_guardians");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.HasIndex(x => x.Email);
        b.HasIndex(x => new { x.StudentId, x.Email }).IsUnique();
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
