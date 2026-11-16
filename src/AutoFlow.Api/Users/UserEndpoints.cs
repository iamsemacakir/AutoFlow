using System.Security.Claims;
using AutoFlow.Api.Auth;
using AutoFlow.Api.Concurrency;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Validation;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Users;

namespace AutoFlow.Api.Users;

/// <summary>Request body for opening a staff account.</summary>
public sealed record CreateUserRequest(string? Email, string? DisplayName, string? Role, string? Password)
    : IValidatableRequest
{
    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("email", Email, EmailAddress.MaxLength);
        errors.RequireText("displayName", DisplayName, User.DisplayNameMaxLength);
        errors.RequireText("role", Role, UserRoleRequest.RoleMaxLength);

        // The minimum length is the password policy's business (422).
        errors.RequireText("password", Password, int.MaxValue);
    }
}

/// <summary>Request body for giving an account another role.</summary>
public sealed record UserRoleRequest(string? Role) : IValidatableRequest
{
    /// <summary>Longest role name (<c>ServiceAdvisor</c>), with room to spare.</summary>
    public const int RoleMaxLength = 30;

    public void Validate(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        errors.RequireText("role", Role, RoleMaxLength);
    }
}

/// <summary>
/// <c>/api/users</c>: the workshop's staff accounts, managed by owners only.
/// Accounts are deactivated rather than deleted. See ADR 0021.
/// </summary>
internal static class UserEndpoints
{
    private const string Tag = "Users";
    private const string GetUserRoute = "GetUser";

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users")
            .WithTags(Tag)
            .WithRequestValidation()
            .RequireAuthorization(Policies.ManageWorkshop);

        users.MapGet("/", ListUsers)
            .WithName("ListUsers")
            .WithSummary("Lists every staff account, active or not, by display name.")
            .Produces<IReadOnlyList<UserDetails>>()
            .ProducesAuthProblems();

        users.MapGet("/{id:guid}", GetUser)
            .WithRowVersion<User>(readOnly: true)
            .WithName(GetUserRoute)
            .WithSummary("Gets a staff account.")
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesAuthProblems();

        users.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Opens a staff account with a role and an initial password.")
            .Produces<UserDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesAuthProblems();

        users.MapPut("/{id:guid}/role", ChangeRole)
            .WithRowVersion<User>()
            .WithName("ChangeUserRole")
            .WithSummary("Gives an account another role; the last active owner keeps theirs.")
            .Produces<UserDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesAuthProblems();

        users.MapPost("/{id:guid}/deactivate", Deactivate)
            .WithRowVersion<User>()
            .WithName("DeactivateUser")
            .WithSummary("Stops an account from signing in; its tokens stop working at once.")
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesAuthProblems();

        users.MapPost("/{id:guid}/reactivate", Reactivate)
            .WithRowVersion<User>()
            .WithName("ReactivateUser")
            .WithSummary("Lets a deactivated account sign in again.")
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesAuthProblems();

        return app;
    }

    private static async Task<IResult> ListUsers(ListUsersHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(new ListUsersQuery(), cancellationToken));

    private static async Task<IResult> GetUser(Guid id, GetUserHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetUserQuery(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        CreateUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateUserCommand(request.Email!, request.DisplayName!, request.Role!, request.Password!),
            cancellationToken);

        return result.Match(
            user => Results.CreatedAtRoute(GetUserRoute, new { id = user.Id }, user),
            error => error.ToProblem());
    }

    private static async Task<IResult> ChangeRole(
        Guid id,
        UserRoleRequest request,
        ChangeUserRoleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ChangeUserRoleCommand(id, request.Role!), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> Deactivate(
        Guid id,
        ClaimsPrincipal user,
        DeactivateUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeactivateUserCommand(id, user.UserId()), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }

    private static async Task<IResult> Reactivate(Guid id, ReactivateUserHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReactivateUserCommand(id), cancellationToken);

        return result.Match(value => Results.Ok(value), error => error.ToProblem());
    }
}
