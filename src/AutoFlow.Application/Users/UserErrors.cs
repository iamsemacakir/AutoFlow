using AutoFlow.Application.Common;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Users;

public static class UserErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("user.not_found", $"User '{userId}' was not found.");

    public static Error DuplicateEmail(string email) =>
        Error.Conflict("user.duplicate_email", $"A user with the e-mail address '{email}' already exists.");

    public static Error UnknownRole(string role) =>
        Error.Validation(
            "user.unknown_role",
            $"'{role}' is not a role; use one of: {string.Join(", ", Enum.GetNames<UserRole>())}.");

    /// <summary>Unknown e-mail, wrong password and deactivated account look the same from outside.</summary>
    public static Error InvalidCredentials() =>
        Error.Unauthorized("auth.invalid_credentials", "The e-mail address or the password is wrong.");

    public static Error WrongCurrentPassword() =>
        Error.Validation("auth.wrong_current_password", "The current password is wrong.");

    public static Error LastOwner() =>
        Error.Conflict(
            "user.last_owner",
            "The workshop needs at least one active owner; make someone else an owner first.");

    public static Error CannotDeactivateSelf() =>
        Error.Conflict("user.cannot_deactivate_self", "You cannot deactivate your own account.");
}
