using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Tests.Fakes;

/// <summary>Issues readable tokens (<c>token-for:{id}:{role}</c>) valid for eight hours.</summary>
public sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public AccessToken Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new AccessToken($"token-for:{user.Id}:{user.Role}", FixedTimeProvider.Default.AddHours(8));
    }
}
