using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Webhooks;
using PayLibre.Domain.Webhooks;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class OutboundWebhookServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<Guid> SeedSchool(TestDb db)
    {
        await using var ctx = db.CreateContext();
        var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeXentalClient(), new FakeNotificationSender(), db.Clock, Opts);
        return (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
    }

    private static OutboundWebhookService Svc(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx, FakeOutboundWebhookSender sender) =>
        new(ctx, db.Tenant, db.Clock, sender);

    [Fact]
    public async Task Subscription_is_created_event_enqueued_and_delivered_signed()
    {
        using var db = new TestDb();
        var schoolId = await SeedSchool(db);
        db.Tenant.TenantId = schoolId;
        var sender = new FakeOutboundWebhookSender { Succeed = true };

        await using (var ctx = db.CreateContext())
        {
            var issued = await Svc(db, ctx, sender).CreateAsync("https://school.example/hook");
            issued.SigningSecret.Should().StartWith("whsec_");
        }

        await using (var ctx = db.CreateContext())
            await Svc(db, ctx, sender).EnqueueAsync(schoolId, "payment.received", new { amountKobo = 5000 });

        await using (var ctx = db.CreateContext())
            (await Svc(db, ctx, sender).DeliverDueAsync()).Should().Be(1);

        sender.Posts.Should().Be(1);
        sender.LastUrl.Should().Be("https://school.example/hook");
        sender.LastPayload.Should().Contain("payment.received").And.Contain("5000");

        await using (var ctx = db.CreateContext())
            (await ctx.WebhookDeliveries.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task Failed_delivery_is_rescheduled_not_dropped()
    {
        using var db = new TestDb();
        var schoolId = await SeedSchool(db);
        db.Tenant.TenantId = schoolId;
        var sender = new FakeOutboundWebhookSender { Succeed = false };

        await using (var ctx = db.CreateContext()) await Svc(db, ctx, sender).CreateAsync("https://school.example/hook");
        await using (var ctx = db.CreateContext()) await Svc(db, ctx, sender).EnqueueAsync(schoolId, "payment.received", new { a = 1 });
        await using (var ctx = db.CreateContext()) await Svc(db, ctx, sender).DeliverDueAsync();

        await using var check = db.CreateContext();
        var d = await check.WebhookDeliveries.IgnoreQueryFilters().SingleAsync();
        d.Status.Should().Be(DeliveryStatus.Pending);      // still queued for retry
        d.Attempts.Should().Be(1);
        d.NextAttemptAtUtc.Should().BeAfter(db.Clock.UtcNow);
    }

    [Fact]
    public async Task Non_https_url_is_rejected()
    {
        using var db = new TestDb();
        db.Tenant.TenantId = await SeedSchool(db);
        await using var ctx = db.CreateContext();
        var act = () => Svc(db, ctx, new FakeOutboundWebhookSender()).CreateAsync("http://insecure.example/hook");
        await act.Should().ThrowAsync<ValidationException>();
    }
}
