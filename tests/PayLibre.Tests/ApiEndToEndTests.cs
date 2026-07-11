using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

/// <summary>
/// Full HTTP end-to-end: register → session cookie → me → create class → create student (DVA) →
/// list, plus an unauthenticated request is rejected. Uses the real ASP.NET pipeline over SQLite,
/// with a faked Xental client. Proves cookie auth (HttpOnly) and the enrolment flow together.
/// </summary>
public sealed class ApiEndToEndTests : IClassFixture<ApiEndToEndTests.PayLibreFactory>
{
    private readonly PayLibreFactory _factory;
    public ApiEndToEndTests(PayLibreFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_login_and_enrol_a_student_over_http()
    {
        var client = _factory.CreateClient();

        // Register — sets HttpOnly session cookies.
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            schoolName = "Acme Academy",
            officialEmail = "owner@acme.edu",
            phone = "08012345678",
            password = "password1",
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        var setCookies = string.Join("; ", register.Headers.TryGetValues("Set-Cookie", out var c) ? c : Array.Empty<string>());
        setCookies.Should().Contain("plb_access").And.Contain("httponly", "session tokens must be HttpOnly cookies");

        // No payout account yet — it is configured from Settings, not at registration.
        (await register.Content.ReadAsStringAsync()).Should().Contain("\"settlementConfigured\":false");

        // Configure settlement from inside the app (Owner) → resolves + persists the payout account.
        var settle = await client.PutAsJsonAsync("/api/v1/schools/settlement", new
        {
            bankName = "Test Bank", bankCode = "999", accountNumber = "0123456789",
        });
        settle.StatusCode.Should().Be(HttpStatusCode.OK);
        (await settle.Content.ReadAsStringAsync()).Should().Contain("\"settlementConfigured\":true").And.Contain("0123456789");

        // Authenticated identity via the cookie.
        var me = await client.GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadAsStringAsync()).Should().Contain("owner@acme.edu");

        // Create a class.
        var classResp = await client.PostAsJsonAsync("/api/v1/classes", new { name = "SS1", session = "2026/2027" });
        classResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var classId = JsonDocument.Parse(await classResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        // Create a student — provisions a DVA via the faked Xental.
        var studentResp = await client.PostAsJsonAsync("/api/v1/students", new
        {
            admissionNo = "ADM-001",
            fullName = "Ada Lovelace",
            classId,
            guardianName = "Mrs Lovelace",
            guardianEmail = "mum@x.com",
        });
        studentResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var student = JsonDocument.Parse(await studentResp.Content.ReadAsStringAsync()).RootElement;
        student.GetProperty("hasVirtualAccount").GetBoolean().Should().BeTrue();
        student.GetProperty("nuban").GetString().Should().NotBeNullOrEmpty();

        // Directory lists the student.
        var list = await client.GetAsync("/api/v1/students");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().Contain("Ada Lovelace");
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var anon = _factory.CreateClient();
        (await anon.GetAsync("/api/v1/students")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v1/classes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public sealed class PayLibreFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public PayLibreFactory() => _connection.Open();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", new string('k', 48));
            builder.UseSetting("Jwt:Issuer", "paylibre");
            builder.UseSetting("Jwt:Audience", "paylibre-dashboard");
            builder.UseSetting("Auth:CookieSecure", "false"); // test client speaks http
            builder.UseSetting("Auth:CookieSameSite", "Lax");

            builder.ConfigureServices(services =>
            {
                // Swap Postgres for the shared in-memory SQLite connection. Strip every EF options
                // descriptor (options, generic options, and the Npgsql UseNpgsql configuration action)
                // then add SQLite with its own EF internal provider so the two providers never clash.
                foreach (var d in services.Where(s => s.ServiceType.FullName?.Contains("DbContextOptions") == true).ToList())
                    services.Remove(d);
                var efSqlite = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();
                services.AddDbContext<PayLibreDbContext>(o => o.UseSqlite(_connection).UseInternalServiceProvider(efSqlite));

                // Swap the real Xental HTTP client for the fake.
                Remove(services, typeof(IXentalClient));
                services.AddSingleton<IXentalClient, FakeXentalClient>();

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider.GetRequiredService<PayLibreDbContext>().Database.EnsureCreated();
            });
        }

        private static void Remove(IServiceCollection services, Type serviceType)
        {
            foreach (var d in services.Where(s => s.ServiceType == serviceType).ToList())
                services.Remove(d);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
