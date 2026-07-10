using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task A_school_cannot_see_another_schools_data()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        var opts = Options.Create(new PayLibreOptions());

        async Task<Guid> Register(string email)
        {
            await using var ctx = db.CreateContext();
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), xental, new FakeNotificationSender(), db.Clock, opts);
            var s = await auth.RegisterAsync(new RegisterSchoolInput("School", email, "0800", "Bank", "999", "0123456789", "password1"));
            return s.School.Id;
        }

        var aId = await Register("a@a.edu");
        var bId = await Register("b@b.edu");

        // School A creates a class.
        db.Tenant.TenantId = aId;
        await using (var ctx = db.CreateContext())
            await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"));

        // School B sees none of A's classes.
        db.Tenant.TenantId = bId;
        await using (var ctx = db.CreateContext())
            (await new ClassService(ctx, db.Tenant).ListAsync()).Should().BeEmpty();

        // School A sees its own.
        db.Tenant.TenantId = aId;
        await using (var ctx = db.CreateContext())
            (await new ClassService(ctx, db.Tenant).ListAsync()).Should().ContainSingle();
    }
}
