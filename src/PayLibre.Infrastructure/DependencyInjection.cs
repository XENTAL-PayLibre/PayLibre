using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Infrastructure.Common;
using PayLibre.Infrastructure.Notifications;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Infrastructure.Security;
using PayLibre.Infrastructure.Xental;

namespace PayLibre.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Options.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<XentalOptions>(configuration.GetSection(XentalOptions.SectionName));
        services.Configure<PayLibreOptions>(configuration.GetSection(PayLibreOptions.SectionName));
        services.Configure<Notifications.ResendOptions>(configuration.GetSection(Notifications.ResendOptions.SectionName));
        services.Configure<Notifications.SmsOptions>(configuration.GetSection(Notifications.SmsOptions.SectionName));
        services.Configure<Maintenance.MaintenanceOptions>(configuration.GetSection(Maintenance.MaintenanceOptions.SectionName));
        services.Configure<Notifications.PushOptions>(configuration.GetSection(Notifications.PushOptions.SectionName));

        // Persistence (Postgres). The DbContext also serves as IApplicationDbContext.
        var connectionString = configuration.GetConnectionString("Default");
        services.AddDbContext<PayLibreDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PayLibreDbContext>());

        // Cross-cutting.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddHttpClient(); // used by the notification sender (Resend/Termii/Twilio) + outbound webhooks
        services.AddScoped<INotificationSender, NotificationSender>();
        services.AddScoped<IOutboundWebhookSender, Webhooks.OutboundWebhookSender>();
        // Dedicated client for outbound webhooks: redirects disabled (SSRF hardening) + a send timeout.
        services.AddHttpClient(Webhooks.OutboundWebhookSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IPushSender, Notifications.FcmPushSender>();

        // Xental integration — the only external dependency.
        services.AddHttpClient<IXentalClient, XentalClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<XentalOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
