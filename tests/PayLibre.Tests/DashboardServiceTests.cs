using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Dashboard;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Payments;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class DashboardServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private static readonly DateTimeOffset Due = DateTimeOffset.Parse("2026-12-31T00:00:00Z");

    [Fact]
    public async Task Overview_reflects_invoiced_collected_and_recent_payments()
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

        Guid classId, catId; string accountRef;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) catId = (await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"))).Id;
        await using (var ctx = db.CreateContext())
            accountRef = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Guardian", null, null))).XentalAccountRef!;
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Tuition", catId, classId, null, "First", 40_000, Due));
        await using (var ctx = db.CreateContext())
            await new ReconciliationService(ctx, db.Clock).ProcessDepositAsync(accountRef, 15_000, 14_800, "n-1", "Parent", db.Clock.UtcNow);

        await using var check = db.CreateContext();
        var overview = await new DashboardService(check, db.Tenant, db.Clock).GetOverviewAsync();

        overview.TotalStudents.Should().Be(1);
        overview.InvoicedKobo.Should().Be(40_000);
        overview.CollectedKobo.Should().Be(15_000);
        overview.OutstandingKobo.Should().Be(25_000);
        overview.Payments.Should().Be(1);
        overview.Recent.Should().ContainSingle().Which.StudentName.Should().Be("Ada");
        overview.RevenueSeries.Should().HaveCount(6);
    }
}
