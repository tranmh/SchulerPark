namespace SchulerPark.Tests.Integration;

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Api.Middleware;
using SchulerPark.Core.Exceptions;

[Trait("Category", "Integration")]
public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(HttpContext Context, JsonElement Body)> InvokeThrowing(Exception ex)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw ex, NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.TraceIdentifier = "trace-123";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, doc.RootElement.Clone());
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var called = false;
        var middleware = new ExceptionHandlingMiddleware(
            ctx => { called = true; ctx.Response.StatusCode = 204; return Task.CompletedTask; },
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task InvokeAsync_ValidationExceptionWithCodeAndParams_Returns400WithExtensions()
    {
        var (ctx, body) = await InvokeThrowing(new ValidationException(
            "Already booked elsewhere", "booking_duplicate_other_location",
            new Dictionary<string, object?> { ["location"] = "Göppingen" }));

        ctx.Response.StatusCode.Should().Be(400);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Bad Request");
        body.GetProperty("detail").GetString().Should().Be("Already booked elsewhere");
        body.GetProperty("instance").GetString().Should().Be("/api/test");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("code").GetString().Should().Be("booking_duplicate_other_location");
        body.GetProperty("params").GetProperty("location").GetString().Should().Be("Göppingen");
    }

    [Fact]
    public async Task InvokeAsync_ValidationExceptionWithoutCode_OmitsCodeAndParams()
    {
        var (ctx, body) = await InvokeThrowing(new ValidationException("bad input"));

        ctx.Response.StatusCode.Should().Be(400);
        body.TryGetProperty("code", out _).Should().BeFalse();
        body.TryGetProperty("params", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ValidationExceptionWithEmptyParams_OmitsParams()
    {
        var (_, body) = await InvokeThrowing(new ValidationException(
            "bad", "some_code", new Dictionary<string, object?>()));

        body.GetProperty("code").GetString().Should().Be("some_code");
        body.TryGetProperty("params", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404WithoutCode()
    {
        var (ctx, body) = await InvokeThrowing(new NotFoundException("Booking not found"));

        ctx.Response.StatusCode.Should().Be(404);
        body.GetProperty("title").GetString().Should().Be("Not Found");
        body.GetProperty("detail").GetString().Should().Be("Booking not found");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.TryGetProperty("code", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ForbiddenException_Returns403WithoutCode()
    {
        var (ctx, body) = await InvokeThrowing(new ForbiddenException("Not yours"));

        ctx.Response.StatusCode.Should().Be(403);
        body.GetProperty("title").GetString().Should().Be("Forbidden");
        body.GetProperty("detail").GetString().Should().Be("Not yours");
        body.TryGetProperty("code", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnexpectedException_Returns500WithGenericDetail()
    {
        var (ctx, body) = await InvokeThrowing(new InvalidOperationException("secret db password leaked"));

        ctx.Response.StatusCode.Should().Be(500);
        body.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.GetRawText().Should().NotContain("secret");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    // Expectation B7 (CLAUDE.md:112-114): errors are returned as application/problem+json.
    // WriteAsJsonAsync(problem) without an explicit content type resets ContentType to
    // "application/json; charset=utf-8", overriding the value the middleware set just before.
    [Fact(Skip = "aqaa: suspected bug - ExceptionHandlingMiddleware responds with application/json instead of application/problem+json (B7)")]
    public async Task InvokeAsync_AnyMappedException_UsesProblemJsonContentType()
    {
        foreach (var ex in new Exception[]
                 {
                     new ValidationException("bad"), new NotFoundException("nf"),
                     new ForbiddenException("fb"), new InvalidOperationException("boom")
                 })
        {
            var (ctx, _) = await InvokeThrowing(ex);
            ctx.Response.ContentType.Should().StartWith("application/problem+json");
        }
    }
}
