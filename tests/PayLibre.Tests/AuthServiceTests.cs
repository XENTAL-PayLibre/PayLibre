using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Domain.Schools;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class AuthServiceTests
{
    private static AuthService Auth(PayLibreDbContext ctx, TestDb db, PayLibre.Application.Common.Interfaces.IXentalClient xental) =>
        new(ctx, new FakePasswordHasher(), new FakeTokenService(), xental, new FakeNotificationSender(), db.Clock, Options.Create(new PayLibreOptions()));

    private static RegisterSchoolInput Reg(string email = "owner@acme.edu") =>
        new("Acme Academy", email, "08012345678", "Test Bank", "999", "0123456789", "password1");

    [Fact]
    public async Task Register_creates_school_owner_refresh_token_and_links_a_xental_submerchant()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        await using var ctx = db.CreateContext();

        var session = await Auth(ctx, db, xental).RegisterAsync(Reg());

        session.School.Status.Should().Be(SchoolStatus.Active);
        session.School.XentalSubMerchantRef.Should().NotBeNullOrEmpty();
        session.School.SettlementAccountName.Should().Be("RESOLVED 0123456789");
        session.User.Role.Should().Be(SchoolRole.Owner);
        xental.SubMerchantsCreated.Should().Be(1);

        await using var check = db.CreateContext();
        (await check.Schools.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await check.SchoolUsers.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await check.RefreshTokens.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email()
    {
        using var db = new TestDb();
        await using (var ctx = db.CreateContext()) await Auth(ctx, db, new FakeXentalClient()).RegisterAsync(Reg());

        await using var ctx2 = db.CreateContext();
        var act = () => Auth(ctx2, db, new FakeXentalClient()).RegisterAsync(Reg("OWNER@acme.edu"));
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Register_rolls_back_when_xental_setup_fails()
    {
        using var db = new TestDb();
        // A client whose sub-merchant setup always throws.
        var failing = new ThrowingXental();
        await using var ctx = db.CreateContext();

        var act = () => Auth(ctx, db, failing).RegisterAsync(Reg());
        await act.Should().ThrowAsync<UpstreamException>();

        await using var check = db.CreateContext();
        (await check.Schools.IgnoreQueryFilters().CountAsync()).Should().Be(0, "nothing should persist if Xental setup fails");
    }

    [Fact]
    public async Task Login_succeeds_with_correct_password_and_fails_otherwise()
    {
        using var db = new TestDb();
        await using (var ctx = db.CreateContext()) await Auth(ctx, db, new FakeXentalClient()).RegisterAsync(Reg());

        await using (var ctx = db.CreateContext())
        {
            var ok = await Auth(ctx, db, new FakeXentalClient()).LoginAsync("owner@acme.edu", "password1");
            ok.User.Email.Should().Be("owner@acme.edu");
        }
        await using (var ctx = db.CreateContext())
        {
            var bad = () => Auth(ctx, db, new FakeXentalClient()).LoginAsync("owner@acme.edu", "wrong");
            await bad.Should().ThrowAsync<AuthenticationException>();
        }
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_revokes_the_old_one()
    {
        using var db = new TestDb();
        IssuedSession login;
        await using (var ctx = db.CreateContext())
        {
            await Auth(ctx, db, new FakeXentalClient()).RegisterAsync(Reg());
        }
        await using (var ctx = db.CreateContext())
            login = await Auth(ctx, db, new FakeXentalClient()).LoginAsync("owner@acme.edu", "password1");

        IssuedSession refreshed;
        await using (var ctx = db.CreateContext())
            refreshed = await Auth(ctx, db, new FakeXentalClient()).RefreshAsync(login.RefreshToken);
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);

        // The old token can no longer be used.
        await using (var ctx = db.CreateContext())
        {
            var reuse = () => Auth(ctx, db, new FakeXentalClient()).RefreshAsync(login.RefreshToken);
            await reuse.Should().ThrowAsync<AuthenticationException>();
        }
    }

    private static AuthService AuthWith(PayLibreDbContext ctx, TestDb db, FakeNotificationSender notifier) =>
        new(ctx, new FakePasswordHasher(), new FakeTokenService(), new FakeXentalClient(), notifier, db.Clock, Options.Create(new PayLibreOptions()));

    [Fact]
    public async Task Login_returns_a_bearer_access_token()
    {
        using var db = new TestDb();
        IssuedSession session;
        await using (var ctx = db.CreateContext()) session = await Auth(ctx, db, new FakeXentalClient()).RegisterAsync(Reg());
        session.Access.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Forgot_then_reset_password_lets_the_user_log_in_with_the_new_password()
    {
        using var db = new TestDb();
        var notifier = new FakeNotificationSender();
        await using (var ctx = db.CreateContext()) await Auth(ctx, db, new FakeXentalClient()).RegisterAsync(Reg());

        await using (var ctx = db.CreateContext()) await AuthWith(ctx, db, notifier).ForgotPasswordAsync("owner@acme.edu");
        notifier.PasswordResets.Should().Be(1);
        var token = notifier.LastResetUrl!.Split("token=")[1];

        await using (var ctx = db.CreateContext()) await AuthWith(ctx, db, notifier).ResetPasswordAsync(token, "newpassword1");

        await using (var ctx = db.CreateContext())
            (await Auth(ctx, db, new FakeXentalClient()).LoginAsync("owner@acme.edu", "newpassword1")).User.Email.Should().Be("owner@acme.edu");
        await using (var ctx = db.CreateContext())
        {
            var oldPw = () => Auth(ctx, db, new FakeXentalClient()).LoginAsync("owner@acme.edu", "password1");
            await oldPw.Should().ThrowAsync<AuthenticationException>();
        }
    }

    [Fact]
    public async Task Forgot_password_is_silent_for_an_unknown_email()
    {
        using var db = new TestDb();
        var notifier = new FakeNotificationSender();
        await using var ctx = db.CreateContext();
        await AuthWith(ctx, db, notifier).ForgotPasswordAsync("nobody@x.com"); // no throw
        notifier.PasswordResets.Should().Be(0);
    }

    /// <summary>A Xental client whose sub-merchant creation always fails, to test rollback.</summary>
    private sealed class ThrowingXental : PayLibre.Application.Common.Interfaces.IXentalClient
    {
        public Task<PayLibre.Application.Common.Interfaces.XentalSubMerchant> CreateSubMerchantAsync(string name, string reference, CancellationToken ct = default)
            => throw new UpstreamException("boom");
        public Task<PayLibre.Application.Common.Interfaces.XentalSubMerchant> SetSubMerchantPayoutAsync(Guid id, string b, string c, string a, int f, CancellationToken ct = default)
            => throw new UpstreamException("boom");
        public Task<PayLibre.Application.Common.Interfaces.XentalVirtualAccount> CreateVirtualAccountAsync(string r, string n, string s, string? e, string? p, long? k, CancellationToken ct = default)
            => throw new UpstreamException("boom");
        public Task<PayLibre.Application.Common.Interfaces.XentalWebhookEndpoint> EnsureWebhookEndpointAsync(string url, CancellationToken ct = default)
            => throw new UpstreamException("boom");
        public Task<IReadOnlyList<PayLibre.Application.Common.Interfaces.XentalBank>> ListBanksAsync(CancellationToken ct = default)
            => throw new UpstreamException("boom");
        public Task<string> LookupBankAccountAsync(string a, string b, CancellationToken ct = default)
            => throw new UpstreamException("boom");
    }
}
