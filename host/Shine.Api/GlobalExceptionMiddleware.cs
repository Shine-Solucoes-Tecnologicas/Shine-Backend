using System.Text.Json;
using Npgsql;
using Shine.Domain;

namespace Shine.Api;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "Domain exception while processing request.");
            await WriteError(context, StatusCodes.Status400BadRequest, "domain_error", ex.Message);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            logger.LogWarning(ex, "Concurrent access-management operation was rejected.");
            await WriteError(
                context,
                StatusCodes.Status409Conflict,
                "concurrent_access_change",
                "A configuração de acesso foi alterada simultaneamente. Atualize os dados e tente novamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while processing request.");
            await WriteError(context, StatusCodes.Status500InternalServerError, "unexpected_error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteError(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new { errors = new[] { new { code, message } } });
    }
}
