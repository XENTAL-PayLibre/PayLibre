using Microsoft.Extensions.DependencyInjection;
using PayLibre.Application.Authentication;
using PayLibre.Application.Enrolment;
using PayLibre.Application.Schools;

namespace PayLibre.Application;

/// <summary>Composition root for the Application layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<Audit.AuditService>();
        services.AddScoped<SchoolService>();
        services.AddScoped<InviteService>();
        services.AddScoped<ApiKeys.ApiKeyService>();
        services.AddScoped<ClassService>();
        services.AddScoped<StudentService>();
        services.AddScoped<SelfEnrolmentService>();
        services.AddScoped<Fees.FeeCategoryService>();
        services.AddScoped<Fees.FeeService>();
        services.AddScoped<Payments.ReconciliationService>();
        services.AddScoped<Payments.WebhookService>();
        services.AddScoped<Payments.RefundService>();
        services.AddScoped<Payments.PaymentService>();
        services.AddScoped<Dashboard.DashboardService>();
        services.AddScoped<Parents.ParentAuthService>();
        services.AddScoped<Parents.ParentService>();
        services.AddScoped<Maintenance.LateFeeService>();
        services.AddScoped<Maintenance.ReminderService>();
        return services;
    }
}
