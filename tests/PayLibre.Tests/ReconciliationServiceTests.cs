using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Payments;
using PayLibre.Domain.Fees;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class ReconciliationServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private static readonly DateTimeOffset DueEarly = DateTimeOffset.Parse("2026-06-30T00:00:00Z");
    private static readonly DateTimeOffset DueLate = DateTimeOffset.Parse("2026-12-31T00:00:00Z");

    /// <summary>Seed school + class + category + one student with two fees (early + late due). Returns the student's accountRef.</summary>
    private static async Task<string> SeedStudentWithTwoFeesAsync(TestDb db, FakeXentalClient x)
    {
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
        }
        db.Tenant.TenantId = schoolId;

        Guid classId, catId;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext()) catId = (await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"))).Id;

        string accountRef;
        await using (var ctx = db.CreateContext())
            accountRef = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Guardian", null, null))).XentalAccountRef!;

        await using (var ctx = db.CreateContext())
        {
            var fees = new FeeService(ctx, db.Tenant, db.Clock);
            await fees.CreateAsync(new FeeSpec("Term 1", catId, classId, null, "First", 10_000, DueEarly));
            await fees.CreateAsync(new FeeSpec("Term 2", catId, classId, null, "Second", 10_000, DueLate));
        }
        return accountRef;
    }

    private static ReconciliationService Recon(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx) => new(ctx, db.Clock, new FakeNotificationSender());

    [Fact]
    public async Task Deposit_is_attributed_oldest_due_first()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var accountRef = await SeedStudentWithTwoFeesAsync(db, x);

        DepositResult result;
        await using (var ctx = db.CreateContext())
            result = await Recon(db, ctx).ProcessDepositAsync(accountRef, 12_000, 11_800, "nomba-1", "Parent", db.Clock.UtcNow);

        result.Status.Should().Be("processed");
        result.AllocatedKobo.Should().Be(12_000);

        await using var check = db.CreateContext();
        var invoices = (await check.StudentFees.IgnoreQueryFilters().ToListAsync()).OrderBy(i => i.DueDateUtc).ToList();
        invoices[0].AmountPaidKobo.Should().Be(10_000);            // earlier-due fully paid first
        invoices[0].Status.Should().Be(FeeStatus.Paid);
        invoices[1].AmountPaidKobo.Should().Be(2_000);             // remainder to the later fee
        invoices[1].Status.Should().Be(FeeStatus.Partial);
    }

    [Fact]
    public async Task Duplicate_transaction_reference_is_ignored()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var accountRef = await SeedStudentWithTwoFeesAsync(db, x);

        await using (var ctx = db.CreateContext()) await Recon(db, ctx).ProcessDepositAsync(accountRef, 10_000, 9_900, "nomba-dup", "P", db.Clock.UtcNow);
        DepositResult second;
        await using (var ctx = db.CreateContext()) second = await Recon(db, ctx).ProcessDepositAsync(accountRef, 10_000, 9_900, "nomba-dup", "P", db.Clock.UtcNow);

        second.Status.Should().Be("duplicate");
        await using var check = db.CreateContext();
        (await check.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(1, "replay must not double-credit");
    }

    [Fact]
    public async Task Deposit_to_an_unknown_account_is_reported_and_not_recorded()
    {
        using var db = new TestDb();
        await SeedStudentWithTwoFeesAsync(db, new FakeXentalClient());

        await using var ctx = db.CreateContext();
        var result = await Recon(db, ctx).ProcessDepositAsync("stu_unknown", 5_000, 5_000, "nomba-x", null, db.Clock.UtcNow);
        result.Status.Should().Be("unknown-account");
        (await ctx.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Overpayment_settles_all_invoices_and_records_the_full_payment()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var accountRef = await SeedStudentWithTwoFeesAsync(db, x); // total outstanding 20,000

        DepositResult result;
        await using (var ctx = db.CreateContext())
            result = await Recon(db, ctx).ProcessDepositAsync(accountRef, 25_000, 24_700, "nomba-over", "P", db.Clock.UtcNow);

        result.AllocatedKobo.Should().Be(20_000);                  // only outstanding gets allocated
        await using var check = db.CreateContext();
        (await check.StudentFees.IgnoreQueryFilters().ToListAsync()).Should().OnlyContain(i => i.Status == FeeStatus.Paid);
        (await check.Payments.IgnoreQueryFilters().SingleAsync()).AmountKobo.Should().Be(25_000); // full amount recorded
    }
}
