using AutoFlow.Application.Abstractions;

namespace AutoFlow.Application.Tests.Fakes;

/// <summary>A reversible stand-in for the real hasher, so tests can read what was stored.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public int Verifications { get; private set; }

    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string passwordHash, string password)
    {
        Verifications++;
        return passwordHash == Hash(password);
    }
}
