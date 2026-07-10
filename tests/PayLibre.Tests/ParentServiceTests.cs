using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Parents;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class ParentServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private static readonly DateTimeOffset Due = DateTimeOffset.Parse("2026-12-31T00:00:00Z");
    private const string Guardian = "mum@x.com";

    /// <summary>Seed a school + class + student (guardian mum@x.com) + a fee. Returns the studentId.</summary>
    private static async Task<Guid> SeedChildWithFeeAsync(TestDb db, FakeXentalClient x)
    {
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "Bank", "999", "0123456789", "password1"))).School.Id;
        }
        db.Tenant.TenantId = schoolId;
        Guid classId, catId, studentId;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) catId = (await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"))).Id;
        await using (var ctx = db.CreateContext())
            studentId = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Mum", "0800", Guardian))).Id;
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Tuition", catId, classId, null, "First", 30_000, Due));
        return studentId;
    }

    private static ParentAuthService AuthSvc(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx) =>
        new(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeNotificationSender());

    [Fact]
    public async Task Parent_sees_only_their_own_children_with_outstanding_and_account()
    {
        using var db = new TestDb();
        await SeedChildWithFeeAsync(db, new FakeXentalClient());
        db.Tenant.TenantId = null; // parent API is global

        await using (var ctx = db.CreateContext()) await AuthSvc(db, ctx).RegisterAsync(Guardian, "password1", "Mum", "0800");

        await using (var ctx = db.CreateContext())
        {
            var children = await new ParentService(ctx).GetChildrenAsync(Guardian);
            children.Should().ContainSingle();
            children[0].FullName.Should().Be("Ada");
            children[0].OutstandingKobo.Should().Be(30_000);
            children[0].Nuban.Should().NotBeNullOrEmpty();
        }

        // A different parent sees nothing.
        await using (var ctx = db.CreateContext())
            (await new ParentService(ctx).GetChildrenAsync("stranger@x.com")).Should().BeEmpty();
    }

    [Fact]
    public async Task Parent_gets_pending_fees_and_payment_details_for_their_child()
    {
        using var db = new TestDb();
        var studentId = await SeedChildWithFeeAsync(db, new FakeXentalClient());
        db.Tenant.TenantId = null;

        await using (var ctx = db.CreateContext())
        {
            var fees = await new ParentService(ctx).GetChildFeesAsync(Guardian, studentId, openOnly: true);
            fees.Should().ContainSingle().Which.OutstandingKobo.Should().Be(30_000);

            var details = await new ParentService(ctx).GetPaymentDetailsAsync(Guardian, studentId);
            details.OutstandingKobo.Should().Be(30_000);
            details.Nuban.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Parent_cannot_access_a_child_that_is_not_theirs()
    {
        using var db = new TestDb();
        var studentId = await SeedChildWithFeeAsync(db, new FakeXentalClient());
        db.Tenant.TenantId = null;

        await using var ctx = db.CreateContext();
        var act = () => new ParentService(ctx).GetPaymentDetailsAsync("stranger@x.com", studentId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Parent_login_requires_correct_password()
    {
        using var db = new TestDb();
        db.Tenant.TenantId = null;
        await using (var ctx = db.CreateContext()) await AuthSvc(db, ctx).RegisterAsync("p@x.com", "password1", "P", null);
        await using (var ctx = db.CreateContext())
            (await AuthSvc(db, ctx).LoginAsync("p@x.com", "password1")).Access.Token.Should().NotBeNullOrEmpty();
        await using (var ctx = db.CreateContext())
        {
            var bad = () => AuthSvc(db, ctx).LoginAsync("p@x.com", "wrong");
            await bad.Should().ThrowAsync<AuthenticationException>();
        }
    }
}
