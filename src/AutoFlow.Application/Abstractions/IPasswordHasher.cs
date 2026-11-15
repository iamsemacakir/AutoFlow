namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Turns passwords into salted, slow hashes and checks passwords against
/// them. The hash string carries everything needed to verify it (algorithm,
/// work factor, salt), so the work factor can be raised later.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>True if <paramref name="password"/> produces <paramref name="passwordHash"/>.</summary>
    bool Verify(string passwordHash, string password);
}
