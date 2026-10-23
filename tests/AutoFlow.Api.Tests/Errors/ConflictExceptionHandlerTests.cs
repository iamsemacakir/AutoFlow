using System.Text.Json;
using AutoFlow.Api.Errors;
using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoFlow.Api.Tests.Errors;

public sealed class ConflictExceptionHandlerTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    [Fact]
    public async Task Concurrency_conflict_becomes_a_409_problem_with_a_stable_code()
    {
        var (handled, context, problem) = await HandleAsync(
            new ConcurrencyConflictException("stale", new InvalidOperationException()));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal("Concurrent update", problem.GetProperty("title").GetString());
        Assert.Equal(ConflictExceptionHandler.ConcurrencyConflictCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unique_violation_becomes_a_409_problem_without_database_details()
    {
        var (handled, context, problem) = await HandleAsync(
            new UniqueConstraintViolationException("duplicate", "ux_vehicles_vin", new InvalidOperationException()));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(ConflictExceptionHandler.DuplicateCode, problem.GetProperty("code").GetString());
        Assert.DoesNotContain("ux_vehicles_vin", problem.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_exceptions_are_left_to_the_next_handler()
    {
        var context = NewContext();
        var handler = NewHandler();

        Assert.False(await handler.TryHandleAsync(context, new DomainException("rule"), CancellationToken.None));
        Assert.False(await handler.TryHandleAsync(context, new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(0, context.Response.Body.Length);
    }

    public void Dispose() => _services.Dispose();

    private async Task<(bool Handled, HttpContext Context, JsonElement Problem)> HandleAsync(Exception exception)
    {
        var context = NewContext();

        var handled = await NewHandler().TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (handled, context, document.RootElement.Clone());
    }

    private ConflictExceptionHandler NewHandler() => new(
        _services.GetRequiredService<IProblemDetailsService>(),
        NullLogger<ConflictExceptionHandler>.Instance);

    private DefaultHttpContext NewContext() => new()
    {
        RequestServices = _services,
        Request = { Headers = { Accept = "application/json" } },
        Response = { Body = new MemoryStream() },
    };
}
