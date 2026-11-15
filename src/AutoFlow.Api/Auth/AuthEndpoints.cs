using System.Security.Claims;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Users;

namespace AutoFlow.Api.Auth;

/// <summary>Request body for signing in.</summary>
public sealed record SignInRequest(string? Email, string? Password) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("email", Email, EmailAddress.MaxLength);
        errors.RequireText("password", Password, PasswordPolicy.MaxLength);
    }
}

/// <summary>Request body for changing one's own password.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword) : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // The length of the new password is the policy's business (422).
        errors.RequireText("currentPassword", CurrentPassword, PasswordPolicy.MaxLength);
        errors.RequireText("newPassword", NewPassword, int.MaxValue);
    }
}

/// <summary>
/// <c>/api/auth</c>: signing in (the one endpoint open to everyone) and the
/// signed-in user's own account. The token goes into every other request as
/// <c>Authorization: Bearer …</c>. See ADR 0021.
/// </summary>
internal static class AuthEndpoints
{
    private const string Tag = "Authentication";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags(Tag).WithRequestValidation();

        auth.MapPost("/token", SignIn)
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Signs in with e-mail address and password and returns a bearer token.")
            .Produces<SignedIn>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapGet("/me", GetCurrentUser)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Gets the signed-in user's account.")
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/me/password", ChangePassword)
            .RequireAuthorization()
            .WithName("ChangePassword")
            .WithSummary("Changes the signed-in user's password; the current password is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> SignIn(SignInRequest request, SignInHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new SignInCommand(request.Email!, request.Password!), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> GetCurrentUser(
        ClaimsPrincipal user,
        GetUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetUserQuery(user.UserId()), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> ChangePassword(
        ClaimsPrincipal user,
        ChangePasswordRequest request,
        ChangePasswordHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ChangePasswordCommand(user.UserId(), request.CurrentPassword!, request.NewPassword!),
            cancellationToken);

        return result.Match(_ => Results.NoContent(), error => error.ToProblem());
    }
}
