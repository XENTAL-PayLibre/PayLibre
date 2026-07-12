using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Fees;
using PayLibre.Application.Parents;
using PayLibre.Application.Payments;
using PayLibre.Domain.Payments;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class DisputeServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());
    private const string Guardian = "mum@x.com";

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
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Mum", null, Guardian))).XentalAccountRef!;
        await using (var ctx = db.CreateContext())
            await new FeeService(ctx, db.Tenant, db.Clock).CreateAsync(new FeeSpec("Term 1", catId, classId, null, "First", 10_000, db.Clock.UtcNow.AddDays(30)));
        await using (var ctx = db.CreateContext())
            await new ReconciliationService(ctx, db.Clock, new FakeNotificationSender()).ProcessDepositAsync(accountRef, 8_000, 7_900, "txn-1", "Parent", db.Clock.UtcNow);
        Guid paymentId;
        await using (var ctx = db.CreateContext()) paymentId = (await ctx.Payments.IgnoreQueryFilters().SingleAsync()).Id;
        return (schoolId, paymentId);
    }

    [Fact]
    public async Task Parent_raises_a_dispute_staff_see_and_resolve_it()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        var (schoolId, paymentId) = await SeedPayment(db, x);

        // Parent raises (global).
        db.Tenant.TenantId = null;
        Guid disputeId;
        await using (var ctx = db.CreateContext())
            disputeId = (await new DisputeService(ctx, db.Tenant, db.Clock).RaiseAsync(Guardian, paymentId, "I didn't get credited")).Id;

        // A stranger cannot raise on this payment.
        await using (var ctx = db.CreateContext())
        {
            var bad = () => new DisputeService(ctx, db.Tenant, db.Clock).RaiseAsync("stranger@x.com", paymentId, "mine");
            await bad.Should().ThrowAsync<NotFoundException>();
        }

        // Duplicate open dispute rejected.
        await using (var ctx = db.CreateContext())
        {
            var dup = () => new DisputeService(ctx, db.Tenant, db.Clock).RaiseAsync(Guardian, paymentId, "again");
            await dup.Should().ThrowAsync<ConflictException>();
        }

        // Staff (tenant-scoped) see + resolve it.
        db.Tenant.TenantId = schoolId;
        db.Tenant.UserEmail = "bursar@acme.edu";
        await using (var ctx = db.CreateContext())
            (await new DisputeService(ctx, db.Tenant, db.Clock).ListAsync(openOnly: true)).Should().ContainSingle();
        await using (var ctx = db.CreateContext())
            (await new DisputeService(ctx, db.Tenant, db.Clock).ResolveAsync(disputeId, accepted: true, "credited manually"))
                .Status.Should().Be(DisputeStatus.Resolved);
        await using (var ctx = db.CreateContext())
            (await new DisputeService(ctx, db.Tenant, db.Clock).ListAsync(openOnly: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task Parent_receipt_is_scoped_to_own_children()
    {
        using var db = new TestDb();
        var (_, paymentId) = await SeedPayment(db, new FakeXentalClient());
        db.Tenant.TenantId = null;

        await using (var ctx = db.CreateContext())
        {
            var r = await new ParentService(ctx).GetReceiptAsync(Guardian, paymentId);
            r.StudentName.Should().Be("Ada");
            r.AmountKobo.Should().Be(8_000);
            r.Reference.Should().Be("txn-1");
        }
        await using (var ctx = db.CreateContext())
        {
            var bad = () => new ParentService(ctx).GetReceiptAsync("stranger@x.com", paymentId);
            await bad.Should().ThrowAsync<NotFoundException>();
        }
    }
}
