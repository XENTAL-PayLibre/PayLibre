using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Schools;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class SchoolServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    [Fact]
    public async Task Settlement_is_not_set_at_registration_and_is_configured_from_the_app()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();

        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), xental, new FakeNotificationSender(), db.Clock, Opts);
            var session = await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@acme.edu", "08000000000", "password1"));
            schoolId = session.School.Id;
            session.School.SettlementConfigured.Should().BeFalse();
            session.School.SettlementAccountNumber.Should().BeNull();
        }
        xental.SubMerchantsCreated.Should().Be(1);

        db.Tenant.TenantId = schoolId;
        await using (var ctx = db.CreateContext())
        {
            var svc = new SchoolService(ctx, db.Tenant, xental, Opts);
            var updated = await svc.UpdateSettlementAsync("Test Bank", "999", "0123456789");
            updated.SettlementConfigured.Should().BeTrue();
            updated.SettlementBankName.Should().Be("Test Bank");
            updated.SettlementAccountNumber.Should().Be("0123456789");
            updated.SettlementAccountName.Should().Be("RESOLVED 0123456789");
        }

        // Persisted across a fresh context.
        await using (var ctx = db.CreateContext())
        {
            var svc = new SchoolService(ctx, db.Tenant, xental, Opts);
            (await svc.GetCurrentAsync()).SettlementAccountNumber.Should().Be("0123456789");
        }
    }
}
