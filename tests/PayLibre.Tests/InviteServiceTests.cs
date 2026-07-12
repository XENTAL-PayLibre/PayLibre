using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Schools;
using PayLibre.Domain.Schools;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class InviteServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<Guid> SeedSchool(TestDb db)
    {
        await using var ctx = db.CreateContext();
        var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeXentalClient(), new FakeNotificationSender(), db.Clock, Opts);
        return (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "owner@acme.edu", "0800", "password1"))).School.Id;
    }

    private static InviteService Svc(TestDb db, PayLibre.Infrastructure.Persistence.PayLibreDbContext ctx, FakeNotificationSender notifier) =>
        new(ctx, db.Tenant, new FakePasswordHasher(), notifier, db.Clock, Opts);

    private static string TokenFrom(string url) => url.Split("token=")[1];

    [Fact]
    public async Task Invite_is_emailed_then_accepted_to_create_a_staff_user_with_the_role()
    {
        using var db = new TestDb();
        var schoolId = await SeedSchool(db);
        db.Tenant.TenantId = schoolId;
        db.Tenant.UserEmail = "owner@acme.edu";
        var notifier = new FakeNotificationSender();

        await using (var ctx = db.CreateContext())
            await Svc(db, ctx, notifier).CreateAsync("acct@acme.edu", SchoolRole.Accountant);
        notifier.Invites.Should().Be(1);
        var token = TokenFrom(notifier.LastInviteUrl!);

        await using (var ctx = db.CreateContext())
            await Svc(db, ctx, notifier).AcceptAsync(token, "password1");

        await using (var ctx = db.CreateContext())
        {
            var user = await ctx.SchoolUsers.IgnoreQueryFilters().SingleAsync(u => u.Email == "acct@acme.edu");
            user.Role.Should().Be(SchoolRole.Accountant);
            user.SchoolId.Should().Be(schoolId);
        }

        // The invite is single-use — a second accept fails.
        await using (var ctx = db.CreateContext())
        {
            var again = () => Svc(db, ctx, notifier).AcceptAsync(token, "password1");
            await again.Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task Owner_cannot_be_invited_and_duplicates_are_rejected()
    {
        using var db = new TestDb();
        var schoolId = await SeedSchool(db);
        db.Tenant.TenantId = schoolId;
        var notifier = new FakeNotificationSender();

        await using (var ctx = db.CreateContext())
        {
            var owner = () => Svc(db, ctx, notifier).CreateAsync("x@acme.edu", SchoolRole.Owner);
            await owner.Should().ThrowAsync<ValidationException>();
        }

        await using (var ctx = db.CreateContext()) await Svc(db, ctx, notifier).CreateAsync("bursar@acme.edu", SchoolRole.Bursar);
        await using (var ctx = db.CreateContext())
        {
            var dup = () => Svc(db, ctx, notifier).CreateAsync("bursar@acme.edu", SchoolRole.Bursar);
            await dup.Should().ThrowAsync<ConflictException>();
        }
    }
}
