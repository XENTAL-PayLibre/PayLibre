using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Schools;
using PayLibre.Domain.Schools;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class ClassTeacherScopingTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    [Fact]
    public async Task Invited_class_teacher_is_assigned_classes_and_only_sees_them()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
        }
        db.Tenant.TenantId = schoolId;

        Guid classA, classB, catId;
        await using (var ctx = db.CreateContext()) classA = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("A", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) classB = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("B", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) catId = (await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"))).Id;
        await using (var ctx = db.CreateContext()) await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).CreateAsync(new StudentInput("A-1", "Ada", classA, null, "G", null, null));
        await using (var ctx = db.CreateContext()) await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).CreateAsync(new StudentInput("B-1", "Bob", classB, null, "G", null, null));
        await using (var ctx = db.CreateContext())
        {
            var fees = new FeeService(ctx, db.Tenant, db.Clock);
            await fees.CreateAsync(new FeeSpec("Fee A", catId, classA, null, "First", 10_000, db.Clock.UtcNow.AddDays(30)));
            await fees.CreateAsync(new FeeSpec("Fee B", catId, classB, null, "First", 10_000, db.Clock.UtcNow.AddDays(30)));
        }

        // Invite a ClassTeacher assigned to class A, then accept it.
        var notifier = new FakeNotificationSender();
        InviteService Inv(PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx) => new(ctx, db.Tenant, new FakePasswordHasher(), notifier, db.Clock, Opts);
        await using (var ctx = db.CreateContext()) await Inv(ctx).CreateAsync("teacher@acme.edu", SchoolRole.ClassTeacher, new[] { classA });
        var token = notifier.LastInviteUrl!.Split("token=")[1];
        await using (var ctx = db.CreateContext()) await Inv(ctx).AcceptAsync(token, "password1");

        await using (var ctx = db.CreateContext())
        {
            var teacher = await ctx.SchoolUsers.IgnoreQueryFilters().SingleAsync(u => u.Email == "teacher@acme.edu");
            teacher.Role.Should().Be(SchoolRole.ClassTeacher);
            (await ctx.SchoolUserClasses.IgnoreQueryFilters().Where(uc => uc.SchoolUserId == teacher.Id).Select(uc => uc.ClassId).ToListAsync())
                .Should().BeEquivalentTo(new[] { classA });
        }

        // Now act AS the class teacher (token carries role + class A).
        db.Tenant.Role = "ClassTeacher";
        db.Tenant.AssignedClassIds = new[] { classA };

        await using (var ctx = db.CreateContext())
        {
            var students = await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).ListAsync(null, null);
            students.Should().ContainSingle().Which.FullName.Should().Be("Ada");   // not Bob (class B)

            var fees = await new FeeService(ctx, db.Tenant, db.Clock).ListAsync();
            fees.Should().ContainSingle().Which.Fee.Name.Should().Be("Fee A");     // not Fee B
        }

        // A class teacher cannot fetch a student outside their class.
        Guid bobId;
        await using (var ctx = db.CreateContext()) bobId = (await ctx.Students.IgnoreQueryFilters().SingleAsync(s => s.AdmissionNo == "B-1")).Id;
        await using (var ctx = db.CreateContext())
        {
            var act = () => new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).GetAsync(bobId);
            await act.Should().ThrowAsync<NotFoundException>();
        }

        // Export is class-scoped too (no whole-school roster leak).
        await using (var ctx = db.CreateContext())
        {
            var csv = await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).ExportCsvAsync();
            csv.Should().Contain("Ada").And.NotContain("Bob");
        }

        // Guardian listing for an out-of-class student is denied.
        await using (var ctx = db.CreateContext())
        {
            var act = () => new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).ListGuardiansAsync(bobId);
            await act.Should().ThrowAsync<NotFoundException>();
        }
    }
}
