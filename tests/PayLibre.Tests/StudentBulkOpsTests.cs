using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Domain.Enrolment;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class StudentBulkOpsTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    [Fact]
    public async Task Promote_moves_students_bulk_status_toggles_and_export_lists_them()
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

        Guid ss1, ss2, adaId, obiId;
        await using (var ctx = db.CreateContext()) ss1 = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) ss2 = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS2", "2027/2028"))).Id;
        await using (var ctx = db.CreateContext())
            adaId = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).CreateAsync(new StudentInput("ADM-1", "Ada", ss1, null, "Mum", null, "mum@x.com"))).Id;
        await using (var ctx = db.CreateContext())
            obiId = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).CreateAsync(new StudentInput("ADM-2", "Obi", ss1, null, "Dad", null, null))).Id;

        // Promote both from SS1 to SS2 (session defaults to the target class's session).
        await using (var ctx = db.CreateContext())
            (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).PromoteAsync(new[] { adaId, obiId }, ss2, null)).Should().Be(2);
        await using (var ctx = db.CreateContext())
        {
            var moved = await ctx.Students.Where(s => s.ClassId == ss2).ToListAsync();
            moved.Should().HaveCount(2);
            moved.All(s => s.Session == "2027/2028").Should().BeTrue();
        }

        // Bulk-deactivate one.
        await using (var ctx = db.CreateContext())
            (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).BulkSetStatusAsync(new[] { adaId }, StudentStatus.Inactive)).Should().Be(1);
        await using (var ctx = db.CreateContext())
            (await ctx.Students.FirstAsync(s => s.Id == adaId)).Status.Should().Be(StudentStatus.Inactive);

        // Export contains the header + both students.
        await using (var ctx = db.CreateContext())
        {
            var csv = await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).ExportCsvAsync();
            csv.Should().Contain("AdmissionNo,FullName,Class").And.Contain("Ada").And.Contain("Obi").And.Contain("SS2");
        }
    }
}
