using System.Text.Json;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Domain.Common;

namespace PayLibre.Api.Middleware;

/// <summary>Maps domain/application exceptions to clean JSON problem responses.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                ValidationException => (StatusCodes.Status400BadRequest, ex.Message),
                AuthenticationException => (StatusCodes.Status401Unauthorized, ex.Message),
                NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflictException => (StatusCodes.Status409Conflict, ex.Message),
                DomainException => (StatusCodes.Status409Conflict, ex.Message),
                UpstreamException => (StatusCodes.Status502BadGateway, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            };

            if (status >= 500) logger.LogError(ex, "Unhandled exception");
            else logger.LogInformation("Request failed ({Status}): {Message}", status, ex.Message);

            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
        }
    }
}
