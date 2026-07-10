using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class SelfEnrolmentServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<(string JoinCode, Guid SchoolId, Guid ClassId)> SeedAsync(TestDb db, FakeXentalClient x)
    {
        string code; Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            var s = await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "Bank", "999", "0123456789", "password1"));
            code = s.School.JoinCode!;
            schoolId = s.School.Id;
        }
        db.Tenant.TenantId = schoolId;
        Guid classId;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        return (code, schoolId, classId);
    }

    private static SelfEnrolmentService Enrol(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx, FakeXentalClient x) =>
        new(ctx, x, new FakeNotificationSender(), db.Clock);

    [Fact]
    public async Task Registration_generates_a_join_code()
    {
        using var db = new TestDb();
        var (code, _, _) = await SeedAsync(db, new FakeXentalClient());
        code.Should().NotBeNullOrWhiteSpace();
        code.Length.Should().Be(8);
    }

    [Fact]
    public async Task Context_by_code_returns_the_school_and_its_classes()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var (code, _, _) = await SeedAsync(db, x);
        db.Tenant.TenantId = null; // public — no tenant

        await using var ctx = db.CreateContext();
        var context = await Enrol(db, ctx, x).GetContextAsync(code);
        context.SchoolName.Should().Be("Acme");
        context.Classes.Should().ContainSingle().Which.Name.Should().Be("SS1");
    }

    [Fact]
    public async Task Parent_can_self_enrol_a_child_which_provisions_a_dva()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var (code, _, classId) = await SeedAsync(db, x);
        db.Tenant.TenantId = null; // public

        await using (var ctx = db.CreateContext())
        {
            var student = await Enrol(db, ctx, x).EnrolAsync(code, new SelfEnrolInput("Baby Ada", classId, "Mum", "0800", "mum@x.com"));
            student.HasVirtualAccount.Should().BeTrue();
            student.SelfEnrolled.Should().BeTrue();
        }

        await using var check = db.CreateContext();
        var s = await check.Students.IgnoreQueryFilters().SingleAsync();
        s.FullName.Should().Be("Baby Ada");
        s.SelfEnrolled.Should().BeTrue();
        s.Nuban.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unknown_join_code_is_not_found()
    {
        using var db = new TestDb();
        await SeedAsync(db, new FakeXentalClient());
        db.Tenant.TenantId = null;
        await using var ctx = db.CreateContext();
        var act = () => Enrol(db, ctx, new FakeXentalClient()).GetContextAsync("BADCODE9");
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
