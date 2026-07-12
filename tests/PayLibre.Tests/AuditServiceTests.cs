using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Audit;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class AuditLogTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    [Fact]
    public async Task Records_events_attributed_to_the_actor_and_scoped_to_the_tenant()
    {
        using var db = new TestDb();
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeXentalClient(), new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
        }

        db.Tenant.TenantId = schoolId;
        db.Tenant.UserId = Guid.NewGuid();
        db.Tenant.UserEmail = "bursar@acme.edu";

        await using (var ctx = db.CreateContext())
            await new AuditService(ctx, db.Tenant).RecordAsync("fee.created", "Fee", Guid.NewGuid(), "Created fee \"Term 1\".");

        await using (var ctx = db.CreateContext())
        {
            var events = await new AuditService(ctx, db.Tenant).ListAsync();
            events.Should().ContainSingle();
            events[0].Action.Should().Be("fee.created");
            events[0].ActorEmail.Should().Be("bursar@acme.edu");
        }

        // A different tenant sees nothing (row-level isolation).
        db.Tenant.TenantId = Guid.NewGuid();
        await using (var ctx = db.CreateContext())
            (await new AuditService(ctx, db.Tenant).ListAsync()).Should().BeEmpty();
    }
}
