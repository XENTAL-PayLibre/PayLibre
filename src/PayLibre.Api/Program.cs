using Microsoft.OpenApi;
using Serilog;
using PayLibre.Application;
using PayLibre.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Serilog: console + rolling file to a dedicated log dir) -----

// LOG_DIRECTORY lets the container point logging at a mounted volume.
var logDirectory = builder.Configuration["LOG_DIRECTORY"]
    ?? Path.Combine(AppContext.BaseDirectory, "logs");

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDirectory, "paylibre-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

// --- Service registration ------------------------------------------------

builder.Services.AddControllers();

// Clean Architecture layers.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Health checks.
builder.Services.AddHealthChecks();

// Swagger / OpenAPI.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PayLibre API",
        Version = "v1",
        Description = "PayLibre backend API."
    });
});

var app = builder.Build();

// Log each HTTP request through Serilog.
app.UseSerilogRequestLogging();

// --- HTTP request pipeline ----------------------------------------------

// Swagger is enabled in every environment so the API is always explorable.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PayLibre API v1");
    options.DocumentTitle = "PayLibre API";
});

// In containers TLS is terminated upstream (proxy/ingress), so only redirect
// to HTTPS when running directly on a host.
var runningInContainer =
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
if (!runningInContainer)
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
