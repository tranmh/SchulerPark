namespace SchulerPark.Api.Middleware;

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
            await WriteProblemDetails(context, "Bad Request", ex.Message);
        }
        catch (NotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteProblemDetails(context, "Not Found", ex.Message);
        }
        catch (ForbiddenException ex)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await WriteProblemDetails(context, "Forbidden", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteProblemDetails(context, "Internal Server Error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemDetails(HttpContext context, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = context.Response.StatusCode
        };
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
