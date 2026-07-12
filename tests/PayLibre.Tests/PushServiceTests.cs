using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Parents;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class PushServiceTests
{
    [Fact]
    public async Task Registers_device_notifies_it_then_unregisters()
    {
        using var db = new TestDb();
        db.Tenant.TenantId = null; // device tokens are global (parent-keyed)
        var sender = new FakePushSender();

        await using (var ctx = db.CreateContext())
        {
            var push = new PushService(ctx, sender);
            await push.RegisterDeviceAsync("mum@x.com", "tok-1", "android");
            await push.RegisterDeviceAsync("mum@x.com", "tok-1", "android"); // idempotent
        }
        await using (var ctx = db.CreateContext())
            (await ctx.DeviceTokens.CountAsync(d => d.ParentEmail == "mum@x.com")).Should().Be(1);

        await using (var ctx = db.CreateContext())
            await new PushService(ctx, sender).NotifyAsync(new[] { "mum@x.com" }, "Payment received", "₦50.00");
        sender.Sends.Should().Be(1);
        sender.LastTokenCount.Should().Be(1);

        await using (var ctx = db.CreateContext())
            await new PushService(ctx, sender).UnregisterDeviceAsync("mum@x.com", "tok-1");
        await using (var ctx = db.CreateContext())
            await new PushService(ctx, sender).NotifyAsync(new[] { "mum@x.com" }, "x", "y");
        sender.Sends.Should().Be(1); // no devices left → no new send
    }
}
