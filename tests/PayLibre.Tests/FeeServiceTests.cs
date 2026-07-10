using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Domain.Fees;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class FeeServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private static readonly DateTimeOffset FutureDue = DateTimeOffset.Parse("2026-12-31T00:00:00Z");

    private static async Task<Guid> SeedSchoolAsync(TestDb db, FakeXentalClient x)
    {
        await using var ctx = db.CreateContext();
        var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
        var s = await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@acme.edu", "08000000000", "Bank", "999", "0123456789", "password1"));
        db.Tenant.TenantId = s.School.Id;
        return s.School.Id;
    }

    /// <summary>Seed a class + N active students; returns (classId, categoryId).</summary>
    private static async Task<(Guid ClassId, Guid CategoryId)> SeedClassCategoryStudentsAsync(TestDb db, FakeXentalClient x, int students)
    {
        Guid classId, catId;
        await using (var ctx = db.CreateContext())
            classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext())
            catId = (await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"))).Id;
        for (var i = 0; i < students; i++)
            await using (var ctx = db.CreateContext())
                await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                    .CreateAsync(new StudentInput($"ADM-{i}", $"Student {i}", classId, null, "Guardian", null, null));
        return (classId, catId);
    }

    private static FeeService Fees(PayLibreDbContext ctx, TestDb db) => new(ctx, db.Tenant, db.Clock);

    [Fact]
    public async Task Creating_a_fee_fans_out_one_invoice_per_active_student()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        await SeedSchoolAsync(db, x);
        var (classId, catId) = await SeedClassCategoryStudentsAsync(db, x, students: 3);

        await using (var ctx = db.CreateContext())
        {
            var (fee, created) = await Fees(ctx, db).CreateAsync(
                new FeeSpec("First Term Tuition", catId, classId, null, "First", 50_000, FutureDue));
            created.Should().Be(3);
            fee.AmountKobo.Should().Be(50_000);
        }

        await using var check = db.CreateContext();
        var invoices = await check.StudentFees.IgnoreQueryFilters().ToListAsync();
        invoices.Should().HaveCount(3);
        invoices.Should().OnlyContain(i => i.AmountKobo == 50_000 && i.Status == FeeStatus.Pending);
    }

    [Fact]
    public async Task Fee_list_and_summary_report_invoiced_collected_outstanding()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        await SeedSchoolAsync(db, x);
        var (classId, catId) = await SeedClassCategoryStudentsAsync(db, x, students: 2);
        await using (var ctx = db.CreateContext())
            await Fees(ctx, db).CreateAsync(new FeeSpec("Tuition", catId, classId, null, "First", 30_000, FutureDue));

        await using var ctx2 = db.CreateContext();
        var list = await Fees(ctx2, db).ListAsync();
        list.Should().ContainSingle();
        list[0].InvoicedKobo.Should().Be(60_000);
        list[0].CollectedKobo.Should().Be(0);
        list[0].OutstandingKobo.Should().Be(60_000);
        list[0].CategoryName.Should().Be("Tuition");

        var summary = await Fees(ctx2, db).SummaryAsync();
        summary.TotalInvoicedKobo.Should().Be(60_000);
        summary.OutstandingKobo.Should().Be(60_000);
        summary.Fees.Should().Be(1);
        summary.StudentFees.Should().Be(2);
    }

    [Fact]
    public async Task Fee_detail_lists_each_students_invoice()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        await SeedSchoolAsync(db, x);
        var (classId, catId) = await SeedClassCategoryStudentsAsync(db, x, students: 2);
        Guid feeId;
        await using (var ctx = db.CreateContext())
            feeId = (await Fees(ctx, db).CreateAsync(new FeeSpec("Tuition", catId, classId, null, "First", 10_000, FutureDue))).Fee.Id;

        await using var ctx2 = db.CreateContext();
        var (fee, invoices) = await Fees(ctx2, db).GetAsync(feeId);
        fee.Name.Should().Be("Tuition");
        invoices.Should().HaveCount(2);
        invoices.Should().OnlyContain(i => i.StudentName.StartsWith("Student") && i.ClassName == "SS1");
    }

    [Fact]
    public async Task Deleting_a_fee_is_blocked_once_an_invoice_has_a_payment()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        await SeedSchoolAsync(db, x);
        var (classId, catId) = await SeedClassCategoryStudentsAsync(db, x, students: 1);
        Guid feeId;
        await using (var ctx = db.CreateContext())
            feeId = (await Fees(ctx, db).CreateAsync(new FeeSpec("Tuition", catId, classId, null, "First", 20_000, FutureDue))).Fee.Id;

        // Simulate a payment against the single invoice.
        await using (var ctx = db.CreateContext())
        {
            var inv = await ctx.StudentFees.IgnoreQueryFilters().FirstAsync();
            inv.Allocate(5_000, db.Clock.UtcNow);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext())
        {
            var del = () => Fees(ctx, db).DeleteAsync(feeId);
            await del.Should().ThrowAsync<ConflictException>();
        }
    }

    [Fact]
    public async Task Duplicate_fee_category_conflicts()
    {
        using var db = new TestDb();
        await SeedSchoolAsync(db, new FakeXentalClient());
        await using (var ctx = db.CreateContext())
            await new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"));
        await using (var ctx = db.CreateContext())
        {
            var dup = () => new FeeCategoryService(ctx, db.Tenant).CreateAsync(new FeeCategoryInput("Tuition"));
            await dup.Should().ThrowAsync<ConflictException>();
        }
    }
}
