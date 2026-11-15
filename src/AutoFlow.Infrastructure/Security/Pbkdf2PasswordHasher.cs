using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoFlow.Application.Abstractions;

namespace AutoFlow.Infrastructure.Security;

/// <summary>
/// PBKDF2 with HMAC-SHA512 from the base class library — no package needed.
/// A hash is stored as <c>pbkdf2-sha512$iterations$salt$hash</c> (Base64),
/// so the iteration count can be raised later without invalidating existing
/// hashes. 210,000 iterations follow the OWASP recommendation for this
/// algorithm. See ADR 0021.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 210_000;

    private const string Scheme = "pbkdf2-sha512";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher()
        : this(DefaultIterations)
    {
    }

    /// <summary>A hasher with a custom work factor; tests use a low one to stay fast.</summary>
    internal Pbkdf2PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, _iterations);

        return string.Join(
            '$',
            Scheme,
            _iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var parts = passwordHash?.Split('$') ?? [];
        if (parts.Length != 4
            || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Derive(password, salt, iterations, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashSize) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, length);
}
