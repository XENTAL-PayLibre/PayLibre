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

        // Persistence (Postgres). The DbContext also serves as IApplicationDbContext.
        var connectionString = configuration.GetConnectionString("Default");
        services.AddDbContext<PayLibreDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PayLibreDbContext>());

        // Cross-cutting.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<INotificationSender, LoggingNotificationSender>();

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
