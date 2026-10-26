using AutoFlow.Api.Errors;

namespace AutoFlow.Api.Validation;

/// <summary>
/// Endpoint filter that validates every <see cref="IValidatableRequest"/>
/// argument and short-circuits with a 400 validation problem listing all
/// field errors at once, so the handler only ever sees well-formed requests.
/// </summary>
internal static class RequestValidation
{
    public static TBuilder WithRequestValidation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(ValidateAsync);

    private static async ValueTask<object?> ValidateAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var errors = new ValidationErrors();
        foreach (var request in context.Arguments.OfType<IValidatableRequest>())
        {
            request.Validate(errors);
        }

        if (!errors.IsEmpty)
        {
            return Results.ValidationProblem(
                errors.ToDictionary(),
                detail: "One or more fields are missing or invalid.",
                title: "Invalid request",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.ValidationFailed });
        }

        return await next(context);
    }
}
