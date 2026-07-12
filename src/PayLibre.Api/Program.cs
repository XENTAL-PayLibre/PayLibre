using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using PayLibre.Api.Auth;
using PayLibre.Api.Authorization;
using PayLibre.Api.Middleware;
using PayLibre.Application;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Infrastructure;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Serilog: console + rolling file) ---------------------------
var logDirectory = builder.Configuration["LOG_DIRECTORY"]
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDirectory, "paylibre-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true));

// --- Services ------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Current-tenant resolution + secure session cookies.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<AuthCookieWriter>();

// JWT auth. The dashboard access token arrives as the HttpOnly `plb_access` cookie (or, for tooling,
// an Authorization: Bearer header).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claims verbatim ("role", "scope", "sub", "email") instead of remapping short names to
        // the long WS-* URIs — so RequireClaim("role", ...) etc. match what JwtTokenService issues.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrWhiteSpace(jwt.SigningKey) ? new string('0', 48) : jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue(AuthCookieWriter.AccessCookie, out var cookie))
                    context.Token = cookie;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.Dashboard, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.Dashboard));
    options.AddPolicy(AuthPolicies.ManageSchool, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.Dashboard)
        .RequireClaim(AuthPolicies.RoleClaim, "Owner", "Admin"));
    options.AddPolicy(AuthPolicies.Parent, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.Parent));
});

// Rate limiting: per-IP fixed windows.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    RateLimitPartition<string> Window(HttpContext ctx, int permit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1) });
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, 300));
    options.AddPolicy("auth", ctx => Window(ctx, 10));
    options.AddPolicy("webhook", ctx => Window(ctx, 600)); // Xental deposit events
});

// CORS — allow the configured frontend origin(s) to send the HttpOnly session cookies.
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
{
    if (corsOrigins.Length > 0)
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddHealthChecks();

// Background sweep: overdue late fees + fee reminders (config section "Maintenance").
builder.Services.AddHostedService<PayLibre.Api.Maintenance.FeeMaintenanceWorker>();

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry()
        .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter())
        .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter());
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PayLibre API",
        Version = "v1",
        Description = """
            PayLibre backend — school fee collection.

            ## Auth (dashboard plane)
            `POST /api/v1/auth/register` and `POST /api/v1/auth/login` set the session as **HttpOnly, Secure
            cookies** (`plb_access` + `plb_refresh`) — tokens are never returned in the body. Every other
            endpoint requires the session cookie, so browser clients must send **`credentials: "include"`**
            on every request (the API allows the configured frontend + `http://localhost:3000` origins with
            credentials). On a `401`, call `POST /api/v1/auth/refresh` to rotate the session; `POST
            /api/v1/auth/logout` clears it. `GET /api/v1/auth/me` returns the current user + school.
            `GET /api/v1/banks` is public (for the settlement-bank dropdown on registration).

            ## Errors
            JSON `{ "error": "message" }` with the matching status: `400` validation, `401` unauthenticated,
            `404` not found, `409` conflict (e.g. duplicate), `502` upstream (Xental) failure.

            ## Typical flow
            register → create classes → add students (each **auto-provisions a dedicated virtual account**)
            or bulk-import via `POST /api/v1/students/import` (multipart CSV). A student's account card is at
            `GET /api/v1/students/{id}/virtual-account`.
            """,
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "PayLibre.Api.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml, includeControllerXmlComments: true);
    options.SupportNonNullableReferenceTypes();
});

var app = builder.Build();
app.UseSerilogRequestLogging();

// Apply EF migrations on startup (Postgres only; tests use SQLite + EnsureCreated).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PayLibreDbContext>();
    if (db.Database.IsNpgsql()) db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PayLibre API v1");
    options.DocumentTitle = "PayLibre API";
});

var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
if (!runningInContainer) app.UseHttpsRedirection();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { } // exposes Program for WebApplicationFactory integration tests
