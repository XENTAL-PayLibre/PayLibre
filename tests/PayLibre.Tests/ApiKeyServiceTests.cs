using FluentAssertions;
using Microsoft.Extensions.Options;
using PayLibre.Application.ApiKeys;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class ApiKeyServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<Guid> SeedSchool(TestDb db)
    {
        await using var ctx = db.CreateContext();
        var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeXentalClient(), new FakeNotificationSender(), db.Clock, Opts);
        return (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
    }

    [Fact]
    public async Task Key_is_issued_once_authenticates_by_hash_and_revokes()
    {
        using var db = new TestDb();
        var schoolId = await SeedSchool(db);
        db.Tenant.TenantId = schoolId;

        string plaintext;
        await using (var ctx = db.CreateContext())
        {
            var issued = await new ApiKeyService(ctx, db.Tenant, db.Clock)
                .CreateAsync("SIS", new[] { ApiScopes.StudentsRead, ApiScopes.StudentsWrite });
            plaintext = issued.PlaintextKey;
            plaintext.Should().StartWith("plb_");
        }

        await using (var ctx = db.CreateContext())
        {
            var key = await new ApiKeyService(ctx, db.Tenant, db.Clock).AuthenticateAsync(plaintext);
            key.Should().NotBeNull();
            key!.SchoolId.Should().Be(schoolId);
            key.HasScope(ApiScopes.StudentsWrite).Should().BeTrue();
            key.HasScope(ApiScopes.PaymentsRead).Should().BeFalse();
        }

        // Wrong key → null.
        await using (var ctx = db.CreateContext())
            (await new ApiKeyService(ctx, db.Tenant, db.Clock).AuthenticateAsync("plb_notarealkey000000000000")).Should().BeNull();

        // Revoke → no longer authenticates.
        Guid keyId;
        await using (var ctx = db.CreateContext()) keyId = (await new ApiKeyService(ctx, db.Tenant, db.Clock).ListAsync())[0].Id;
        await using (var ctx = db.CreateContext()) await new ApiKeyService(ctx, db.Tenant, db.Clock).RevokeAsync(keyId);
        await using (var ctx = db.CreateContext())
            (await new ApiKeyService(ctx, db.Tenant, db.Clock).AuthenticateAsync(plaintext)).Should().BeNull();
    }

    [Fact]
    public async Task Unknown_scope_is_rejected()
    {
        using var db = new TestDb();
        db.Tenant.TenantId = await SeedSchool(db);
        await using var ctx = db.CreateContext();
        var act = () => new ApiKeyService(ctx, db.Tenant, db.Clock).CreateAsync("x", new[] { "students:delete" });
        await act.Should().ThrowAsync<ValidationException>();
    }
}
