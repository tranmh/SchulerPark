namespace SchulerPark.Api.Middleware;

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Core.Exceptions;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteProblemDetails(context, "Bad Request", ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1");
        }
        catch (NotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteProblemDetails(context, "Not Found", ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5");
        }
        catch (ForbiddenException ex)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await WriteProblemDetails(context, "Forbidden", ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.4");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteProblemDetails(context, "Internal Server Error",
                "An unexpected error occurred.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1");
        }
    }

    private static async Task WriteProblemDetails(HttpContext context, string title, string detail, string type)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Detail = detail,
            Status = context.Response.StatusCode,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;

        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
