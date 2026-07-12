using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Payments;
using PayLibre.Domain.Common;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class RefundServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    /// <summary>Seed a school + student + fee, reconcile a deposit, and return (schoolId, paymentId).</summary>
    private static async Task<(Guid schoolId, Guid paymentId)> SeedPayment(TestDb db, FakeXentalClient x)
    {
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
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Mum", null, null))).XentalAccountRef!;
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Term 1", catId, classId, null, "First", 10_000, db.Clock.UtcNow.AddDays(30)));
        await using (var ctx = db.CreateContext())
            await new ReconciliationService(ctx, db.Clock, new FakeNotificationSender())
                .ProcessDepositAsync(accountRef, 15_000, 14_800, "txn-1", "Parent", db.Clock.UtcNow);
        Guid paymentId;
        await using (var ctx = db.CreateContext()) paymentId = (await ctx.Payments.IgnoreQueryFilters().SingleAsync()).Id;
        return (schoolId, paymentId);
    }

    private static RefundService Svc(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx, FakeXentalClient x) =>
        new(ctx, db.Tenant, x, db.Clock);

    [Fact]
    public async Task Refund_requires_a_different_approver_and_executes_against_xental()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient { RefundAmountKobo = 5_000 };
        var (_, paymentId) = await SeedPayment(db, x);
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();

        db.Tenant.UserId = requester; db.Tenant.UserEmail = "maker@acme.edu";
        Guid refundId;
        await using (var ctx = db.CreateContext())
            refundId = (await Svc(db, ctx, x).RequestAsync(paymentId, "overpayment")).Id;

        // Same user cannot approve their own request.
        await using (var ctx = db.CreateContext())
        {
            var self = () => Svc(db, ctx, x).ApproveAsync(refundId);
            await self.Should().ThrowAsync<DomainException>();
        }

        // A different Owner/Admin approves → executes against Xental.
        db.Tenant.UserId = approver; db.Tenant.UserEmail = "checker@acme.edu";
        await using (var ctx = db.CreateContext())
        {
            var r = await Svc(db, ctx, x).ApproveAsync(refundId);
            r.Status.Should().Be(PayLibre.Domain.Payments.RefundStatus.Executed);
            r.AmountKobo.Should().Be(5_000);
        }
        x.RefundsIssued.Should().Be(1);
        x.LastRefundRef.Should().Be("txn-1");
    }

    [Fact]
    public async Task A_second_refund_request_for_the_same_payment_is_rejected()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var (_, paymentId) = await SeedPayment(db, x);
        db.Tenant.UserId = Guid.NewGuid();

        await using (var ctx = db.CreateContext()) await Svc(db, ctx, x).RequestAsync(paymentId, null);
        await using (var ctx = db.CreateContext())
        {
            var dup = () => Svc(db, ctx, x).RequestAsync(paymentId, null);
            await dup.Should().ThrowAsync<ConflictException>();
        }
    }

    [Fact]
    public async Task Rejecting_a_refund_does_not_touch_xental()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var (_, paymentId) = await SeedPayment(db, x);
        db.Tenant.UserId = Guid.NewGuid();

        Guid refundId;
        await using (var ctx = db.CreateContext()) refundId = (await Svc(db, ctx, x).RequestAsync(paymentId, null)).Id;
        await using (var ctx = db.CreateContext())
            (await Svc(db, ctx, x).RejectAsync(refundId, "not valid")).Status.Should().Be(PayLibre.Domain.Payments.RefundStatus.Rejected);
        x.RefundsIssued.Should().Be(0);
    }
}
