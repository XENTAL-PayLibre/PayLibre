using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Payments;
using PayLibre.Domain.Payments;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class WebhookServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private static readonly DateTimeOffset Due = DateTimeOffset.Parse("2026-06-30T00:00:00Z");

    /// <summary>Seed school + class + category + a student (guardian email set) with one 10,000 fee. Returns the accountRef.</summary>
    private static async Task<string> SeedAsync(TestDb db, FakeXentalClient x)
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
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Mum", "0800", "mum@x.com"))).XentalAccountRef!;
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Term 1", catId, classId, null, "First", 10_000, Due));
        db.Tenant.TenantId = null; // webhooks are platform-global
        return accountRef;
    }

    private static string Payload(string accountRef, long amount, string txRef) =>
        "{\"event\":\"deposit.reconciled\",\"data\":{"
        + $"\"accountRef\":\"{accountRef}\",\"amountKobo\":{amount},\"netCreditKobo\":{amount},"
        + $"\"transactionRef\":\"{txRef}\",\"transferName\":\"Parent\",\"occurredAt\":\"2026-07-01T00:00:00Z\""
        + "}}";

    private static WebhookService Svc(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx, FakeNotificationSender notifier) =>
        new(ctx, new ReconciliationService(ctx, db.Clock, notifier), db.Clock);

    [Fact]
    public async Task Ingest_records_the_event_processes_the_deposit_and_emails_a_receipt()
    {
        using var db = new TestDb();
        var accountRef = await SeedAsync(db, new FakeXentalClient());
        var notifier = new FakeNotificationSender();

        WebhookIngestResult result;
        await using (var ctx = db.CreateContext())
            result = await Svc(db, ctx, notifier).IngestXentalAsync(Payload(accountRef, 6_000, "tx-1"), signatureVerified: true);

        result.Status.Should().Be(WebhookStatus.Processed);
        notifier.Receipts.Should().Be(1);
        notifier.LastReceiptAmountKobo.Should().Be(6_000);
        notifier.LastReceiptOutstandingKobo.Should().Be(4_000); // 10,000 fee minus 6,000 paid

        await using var check = db.CreateContext();
        var evt = await check.WebhookEvents.SingleAsync();
        evt.Status.Should().Be(WebhookStatus.Processed);
        evt.Reference.Should().Be("tx-1");
        evt.SignatureValid.Should().BeTrue();
        (await check.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unhandled_event_type_is_recorded_as_ignored_and_not_processed()
    {
        using var db = new TestDb();
        await SeedAsync(db, new FakeXentalClient());
        var notifier = new FakeNotificationSender();

        await using (var ctx = db.CreateContext())
        {
            var r = await Svc(db, ctx, notifier).IngestXentalAsync("""{"event":"payout.settled","data":{}}""", true);
            r.Status.Should().Be(WebhookStatus.Ignored);
        }
        notifier.Receipts.Should().Be(0);
        await using var check = db.CreateContext();
        (await check.WebhookEvents.SingleAsync()).Status.Should().Be(WebhookStatus.Ignored);
        (await check.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_account_is_dead_lettered()
    {
        using var db = new TestDb();
        await SeedAsync(db, new FakeXentalClient());

        await using var ctx = db.CreateContext();
        var r = await Svc(db, ctx, new FakeNotificationSender()).IngestXentalAsync(Payload("stu_doesnotexist", 5_000, "tx-x"), true);
        r.Status.Should().Be(WebhookStatus.Failed);
        (await ctx.WebhookEvents.SingleAsync()).Detail.Should().Be("unknown-account");
    }

    [Fact]
    public async Task Replay_reprocesses_a_dead_lettered_event_and_is_idempotent()
    {
        using var db = new TestDb();
        var accountRef = await SeedAsync(db, new FakeXentalClient());
        var notifier = new FakeNotificationSender();

        // Simulate a stored dead-letter (e.g. it failed on first receipt).
        Guid eventId;
        await using (var ctx = db.CreateContext())
        {
            var evt = new WebhookEvent("xental", Payload(accountRef, 10_000, "tx-replay"), signatureValid: true, db.Clock.UtcNow);
            evt.RecordResult("deposit.reconciled", "tx-replay", WebhookStatus.Failed, "boom", db.Clock.UtcNow);
            ctx.WebhookEvents.Add(evt);
            await ctx.SaveChangesAsync();
            eventId = evt.Id;
        }

        await using (var ctx = db.CreateContext())
            (await Svc(db, ctx, notifier).ReplayAsync(eventId)).Status.Should().Be(WebhookStatus.Processed);
        notifier.Receipts.Should().Be(1);

        // Replaying again must not double-apply the payment.
        await using (var ctx = db.CreateContext())
            (await Svc(db, ctx, notifier).ReplayAsync(eventId)).Status.Should().Be(WebhookStatus.Duplicate);

        await using var check = db.CreateContext();
        (await check.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}
