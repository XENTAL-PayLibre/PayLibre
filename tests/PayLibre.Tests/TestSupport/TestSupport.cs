using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;
using PayLibre.Infrastructure.Persistence;

namespace PayLibre.Tests.TestSupport;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public FakeClock() : this(DateTimeOffset.Parse("2026-01-01T00:00:00Z")) { }
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class FakeTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
    public Guid RequireTenantId() => TenantId ?? throw new InvalidOperationException("No tenant set.");
}

/// <summary>Fast, deterministic password hasher for tests (no BCrypt cost).</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "h:" + password;
    public bool Verify(string password, string hash) => hash == "h:" + password;
}

public sealed class FakeTokenService : ITokenService
{
    public AccessToken IssueAccessToken(SchoolUser user) =>
        new($"token-{user.Id:N}", DateTimeOffset.UtcNow.AddMinutes(15));
}

/// <summary>In-memory stand-in for Xental. Records calls and returns deterministic NUBANs/sub-merchants.</summary>
public sealed class FakeXentalClient : IXentalClient
{
    private long _seq = 9000000000;
    public int SubMerchantsCreated { get; private set; }
    public int VirtualAccountsCreated { get; private set; }
    public List<string> ProvisionedRefs { get; } = new();
    public bool FailNextVirtualAccount { get; set; }

    public Task<XentalSubMerchant> CreateSubMerchantAsync(string name, string reference, CancellationToken ct = default)
    {
        SubMerchantsCreated++;
        return Task.FromResult(new XentalSubMerchant(Guid.NewGuid(), reference, "Active", null));
    }

    public Task<XentalSubMerchant> SetSubMerchantPayoutAsync(Guid subMerchantId, string bankName, string bankCode, string accountNumber, int platformFeeBps, CancellationToken ct = default)
        => Task.FromResult(new XentalSubMerchant(subMerchantId, $"sub_{subMerchantId:N}", "Active", $"RESOLVED {accountNumber}"));

    public Task<XentalVirtualAccount> CreateVirtualAccountAsync(string accountRef, string name, string subMerchantRef, string? email, string? phone, long? expectedAmountKobo, CancellationToken ct = default)
    {
        if (FailNextVirtualAccount) { FailNextVirtualAccount = false; throw new PayLibre.Application.Common.Exceptions.UpstreamException("simulated failure"); }
        VirtualAccountsCreated++;
        ProvisionedRefs.Add(accountRef);
        var nuban = (_seq++).ToString();
        return Task.FromResult(new XentalVirtualAccount(Guid.NewGuid(), accountRef, nuban, "PayLibre Test Bank", $"PayLibre - {name}"));
    }

    public Task<XentalWebhookEndpoint> EnsureWebhookEndpointAsync(string url, CancellationToken ct = default)
        => Task.FromResult(new XentalWebhookEndpoint(Guid.NewGuid(), url, "whsec_test"));

    public Task<IReadOnlyList<XentalBank>> ListBanksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<XentalBank>>(new[] { new XentalBank("PayLibre Test Bank", "999") });

    public Task<string> LookupBankAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default)
        => Task.FromResult($"RESOLVED {accountNumber}");
}

public sealed class FakeNotificationSender : INotificationSender
{
    public int Sent { get; private set; }
    public Task SendVirtualAccountDetailsAsync(string toName, string? email, string? phone, string studentName, string nuban, string bankName, string accountName, CancellationToken ct = default)
    { Sent++; return Task.CompletedTask; }
}

/// <summary>SQLite in-memory database shared across contexts for one test.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PayLibreDbContext> _options;

    public FakeTenantContext Tenant { get; } = new();
    public FakeClock Clock { get; } = new();

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PayLibreDbContext>().UseSqlite(_connection).Options;
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    public PayLibreDbContext CreateContext() => new(_options, Tenant, Clock);

    public void Dispose() => _connection.Dispose();
}
