using AutoFlow.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace AutoFlow.Api.Auth;

/// <summary>
/// Who may do what, as named authorization policies over the user's role.
/// Endpoints name a policy instead of listing roles, so the mapping from
/// roles to abilities lives in one place. See ADR 0021.
/// </summary>
public static class Policies
{
    /// <summary>Any active account: read the plan and work on work orders.</summary>
    public const string Staff = "Staff";

    /// <summary>Owners and service advisors: customers, vehicles, appointments, estimates, invoices.</summary>
    public const string FrontDesk = "FrontDesk";

    /// <summary>Owners only: bays and staff accounts.</summary>
    public const string ManageWorkshop = "ManageWorkshop";

    public static AuthorizationBuilder AddAutoFlowPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Anything without a policy of its own needs at least a valid token, so a new
        // endpoint is never open by accident; public endpoints opt out explicitly.
        return builder
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Staff, policy => policy.RequireRole(Names(UserRole.Owner, UserRole.ServiceAdvisor, UserRole.Mechanic)))
            .AddPolicy(FrontDesk, policy => policy.RequireRole(Names(UserRole.Owner, UserRole.ServiceAdvisor)))
            .AddPolicy(ManageWorkshop, policy => policy.RequireRole(Names(UserRole.Owner)));
    }

    /// <summary>Documents the answers every protected endpoint can give: 401 without a valid token, 403 without the role.</summary>
    public static RouteHandlerBuilder ProducesAuthProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static string[] Names(params UserRole[] roles) => [.. roles.Select(role => role.ToString())];
}
