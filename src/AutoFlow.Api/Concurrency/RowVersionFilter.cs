using AutoFlow.Api.Errors;
using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Common;

namespace AutoFlow.Api.Concurrency;

/// <summary>
/// Conditional requests for the aggregate an endpoint's <c>{id}</c> names
/// (ADR 0024). A successful answer carries the aggregate's version as
/// <c>ETag</c>. A request with <c>If-Match</c> only reaches the use case while
/// the aggregate is still at that version, otherwise it is answered with
/// 412; a change committed by another request after the check makes the
/// save fail with 409 (<c>conflict.concurrent_update</c>). Without
/// <c>If-Match</c> a request behaves as before.
/// </summary>
internal sealed class RowVersionFilter<TAggregate> : IEndpointFilter
    where TAggregate : Entity
{
    public const string RouteKey = "id";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (!Guid.TryParse(http.GetRouteValue(RouteKey)?.ToString(), out var id))
        {
            return await next(context);
        }

        var versions = http.RequestServices.GetRequiredService<IRowVersions>();
        var ifMatch = http.Request.Headers.IfMatch;

        if (ifMatch.Count > 0)
        {
            var current = await versions.GetAsync<TAggregate>(id, http.RequestAborted);
            if (current is not null)
            {
                if (!EntityTags.Matches(ifMatch, current.Value))
                {
                    return PreconditionFailed(current.Value);
                }

                versions.Expect<TAggregate>(id, current.Value);
            }
        }

        var result = await next(context);

        // A deleted aggregate has no version left, so a 204 goes out without a tag.
        if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
        {
            var version = await versions.GetAsync<TAggregate>(id, http.RequestAborted);
            if (version is not null)
            {
                http.Response.Headers.ETag = EntityTags.Format(version.Value);
            }
        }

        return result;
    }

    private static IResult PreconditionFailed(uint current) =>
        Results.Problem(
            detail: "The resource was changed since the version named in If-Match. Reload it and try again.",
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "Precondition failed",
            extensions: new Dictionary<string, object?>
            {
                [ProblemDetailsConventions.CodeKey] = ProblemCodes.PreconditionFailed,
                ["currentETag"] = EntityTags.Format(current),
            });
}

/// <summary>Attaches <see cref="RowVersionFilter{TAggregate}"/> to an endpoint.</summary>
internal static class RowVersionEndpointExtensions
{
    /// <summary>
    /// Sends the version of the <typeparamref name="TAggregate"/> named by
    /// <c>{id}</c> as <c>ETag</c> and, unless the endpoint only reads, honours
    /// <c>If-Match</c> and documents the 412 answer.
    /// </summary>
    public static RouteHandlerBuilder WithRowVersion<TAggregate>(this RouteHandlerBuilder builder, bool readOnly = false)
        where TAggregate : Entity
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<RowVersionFilter<TAggregate>>();
        return readOnly ? builder : builder.ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }
}
