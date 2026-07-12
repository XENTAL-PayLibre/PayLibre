using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Maintenance;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class MaintenanceServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<Guid> Seed(TestDb db, FakeXentalClient x, DateTimeOffset due,
        bool appliesLateFee = true, string? guardianEmail = "mum@x.com", string? guardianPhone = "0800")
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
        await using (var ctx = db.CreateContext())
            await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Mum", guardianPhone, guardianEmail));
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Term 1", catId, classId, null, "First", 10_000, due, appliesLateFee));
        db.Tenant.TenantId = null; // maintenance jobs run platform-global
        return schoolId;
    }

    private static async Task SetLateFee(TestDb db, Guid schoolId, int bps, int grace)
    {
        await using var ctx = db.CreateContext();
        var s = await ctx.Schools.IgnoreQueryFilters().FirstAsync(x => x.Id == schoolId);
        s.ConfigureLateFees(bps, grace);
        await ctx.SaveChangesAsync();
    }

    // ---- Late fees ----

    [Fact]
    public async Task Late_fee_is_applied_once_to_overdue_invoices_past_grace()
    {
        using var db = new TestDb();
        var schoolId = await Seed(db, new FakeXentalClient(), db.Clock.UtcNow.AddDays(-5));
        await SetLateFee(db, schoolId, 500, 3); // 5%, 3-day grace

        await using (var ctx = db.CreateContext())
            (await new LateFeeService(ctx, db.Clock).ApplyDueAsync()).Should().Be(1);

        await using (var ctx = db.CreateContext())
        {
            var sf = await ctx.StudentFees.IgnoreQueryFilters().SingleAsync();
            sf.LateFeeAppliedKobo.Should().Be(500);     // 5% of 10,000
            sf.AmountKobo.Should().Be(10_500);
        }

        // Idempotent — not applied twice.
        await using (var ctx = db.CreateContext())
            (await new LateFeeService(ctx, db.Clock).ApplyDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Late_fee_is_skipped_when_disabled_within_grace_or_opted_out()
    {
        using var db = new TestDb();

        // Disabled (no policy configured).
        var s1 = await Seed(db, new FakeXentalClient(), db.Clock.UtcNow.AddDays(-5));
        await using (var ctx = db.CreateContext())
            (await new LateFeeService(ctx, db.Clock).ApplyDueAsync()).Should().Be(0);

        // Configured, but the invoice is still within the grace period.
        await SetLateFee(db, s1, 500, 10); // grace 10 days; overdue only 5
        await using (var ctx = db.CreateContext())
            (await new LateFeeService(ctx, db.Clock).ApplyDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Late_fee_respects_the_per_fee_opt_out()
    {
        using var db = new TestDb();
        var schoolId = await Seed(db, new FakeXentalClient(), db.Clock.UtcNow.AddDays(-5), appliesLateFee: false);
        await SetLateFee(db, schoolId, 500, 3);
        await using var ctx = db.CreateContext();
        (await new LateFeeService(ctx, db.Clock).ApplyDueAsync()).Should().Be(0);
    }

    // ---- Reminders ----

    [Theory]
    [InlineData(-5, null)]   // 5 days before due → nothing yet
    [InlineData(-2, "pre")]  // within 3 days → pre-due reminder
    [InlineData(0, "od0")]   // due date → first overdue-band reminder
    [InlineData(8, "od1")]   // a week overdue
    [InlineData(60, "od4")]  // far overdue → capped
    public void StageFor_maps_time_to_the_right_reminder(int offsetDays, string? expected)
    {
        var due = DateTimeOffset.Parse("2026-06-30T00:00:00Z");
        ReminderService.StageFor(due, due.AddDays(offsetDays)).Should().Be(expected);
    }

    [Fact]
    public async Task Reminders_send_pre_due_then_overdue_and_never_repeat_a_stage()
    {
        using var db = new TestDb();
        var due = db.Clock.UtcNow.AddDays(2); // due in 2 days → "pre" is due now
        await Seed(db, new FakeXentalClient(), due);
        var notifier = new FakeNotificationSender();

        await using (var ctx = db.CreateContext())
            (await new ReminderService(ctx, db.Clock, notifier).SendDueAsync()).Should().Be(1);
        notifier.Reminders.Should().Be(1);
        notifier.OverdueReminders.Should().Be(0);

        // Same day → the "pre" stage already went out; nothing new.
        await using (var ctx = db.CreateContext())
            (await new ReminderService(ctx, db.Clock, notifier).SendDueAsync()).Should().Be(0);

        // Move past the due date → an overdue reminder goes out.
        db.Clock.UtcNow = due.AddDays(1);
        await using (var ctx = db.CreateContext())
            (await new ReminderService(ctx, db.Clock, notifier).SendDueAsync()).Should().Be(1);
        notifier.OverdueReminders.Should().Be(1);
    }

    [Fact]
    public async Task Reminders_skip_invoices_with_no_guardian_contact()
    {
        using var db = new TestDb();
        await Seed(db, new FakeXentalClient(), db.Clock.UtcNow.AddDays(1), guardianEmail: null, guardianPhone: null);
        var notifier = new FakeNotificationSender();
        await using var ctx = db.CreateContext();
        (await new ReminderService(ctx, db.Clock, notifier).SendDueAsync()).Should().Be(0);
        notifier.Reminders.Should().Be(0);
    }
}
