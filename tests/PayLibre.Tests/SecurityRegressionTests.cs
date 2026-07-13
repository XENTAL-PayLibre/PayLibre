using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Payments;
using PayLibre.Infrastructure.Webhooks;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class SecurityRegressionTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data/iam/")] // cloud metadata
    [InlineData("https://127.0.0.1/hook")]                        // loopback
    [InlineData("https://10.1.2.3/hook")]                         // private
    [InlineData("https://192.168.0.5/hook")]                      // private
    [InlineData("http://example.com/hook")]                       // non-https
    public async Task Outbound_webhook_blocks_internal_or_insecure_destinations(string url)
    {
        var sender = new OutboundWebhookSender(new NoopHttpClientFactory(), NullLogger<OutboundWebhookSender>.Instance);
        var (ok, _, error) = await sender.PostAsync(url, "{}", "secret");
        ok.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Deposit_with_non_positive_amount_is_ignored_and_does_not_burn_the_ref()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        // Minimal seed: school + class + student (with a DVA accountRef).
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
        }
        db.Tenant.TenantId = schoolId;
        Guid classId; string accountRef;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext())
            accountRef = (await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "G", null, null))).XentalAccountRef!;

        await using (var ctx = db.CreateContext())
        {
            var r = await new ReconciliationService(ctx, db.Clock, new FakeNotificationSender()).ProcessDepositAsync(accountRef, 0, 0, "zero-1", "P", db.Clock.UtcNow);
            r.Status.Should().Be("ignored:invalid-amount");
        }
        await using (var ctx = db.CreateContext())
            (await ctx.Payments.IgnoreQueryFilters().AnyAsync()).Should().BeFalse(); // ref not consumed
    }

    [Fact]
    public async Task Csv_export_neutralizes_formula_injection()
    {
        using var db = new TestDb();
        var x = new FakeXentalClient();
        Guid schoolId;
        await using (var ctx = db.CreateContext())
        {
            var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), x, new FakeNotificationSender(), db.Clock, Opts);
            schoolId = (await auth.RegisterAsync(new RegisterSchoolInput("Acme", "o@a.edu", "0800", "password1"))).School.Id;
        }
        db.Tenant.TenantId = schoolId;
        Guid classId;
        await using (var ctx = db.CreateContext()) classId = (await new ClassService(ctx, db.Tenant).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext())
            await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts)
                .CreateAsync(new StudentInput("ADM-1", "=HYPERLINK(\"http://evil\")", classId, null, "G", null, null));

        await using (var ctx = db.CreateContext())
        {
            var csv = await new StudentService(ctx, db.Tenant, x, new FakeNotificationSender(), Opts).ExportCsvAsync();
            csv.Should().NotContain("\n=HYPERLINK").And.NotContain(",=HYPERLINK");
            csv.Should().Contain("'=HYPERLINK"); // prefixed to defuse the formula
        }
    }
}
