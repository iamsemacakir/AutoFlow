using AutoFlow.Infrastructure.Security;

namespace AutoFlow.Infrastructure.Tests;

public class Pbkdf2PasswordHasherTests
{
    // A low work factor keeps the tests fast; the format and the checks are the same.
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    [Fact]
    public void Hash_verifies_against_the_same_password_only()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify(hash, "correct horse battery staple"));
        Assert.False(_hasher.Verify(hash, "correct horse battery stapl"));
        Assert.False(_hasher.Verify(hash, "Correct horse battery staple"));
    }

    [Fact]
    public void Hash_is_salted_and_records_algorithm_and_work_factor()
    {
        var first = _hasher.Hash("same password here");
        var second = _hasher.Hash("same password here");

        Assert.NotEqual(first, second);
        var parts = first.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal(("pbkdf2-sha512", "1000"), (parts[0], parts[1]));
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_made_with_another_work_factor_still_verifies()
    {
        var hash = new Pbkdf2PasswordHasher(iterations: 2_000).Hash("rotate the factor");

        Assert.True(_hasher.Verify(hash, "rotate the factor"));
    }

    [Fact]
    public void Default_work_factor_follows_the_owasp_recommendation()
    {
        var hash = new Pbkdf2PasswordHasher().Hash("x");

        Assert.Equal("210000", hash.Split('$')[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("bcrypt$10$abc$def")]
    [InlineData("pbkdf2-sha512$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha512$1000$not base64!$aGFzaA==")]
    [InlineData("pbkdf2-sha512$1000$c2FsdA==")]
    public void Malformed_or_foreign_hashes_never_verify(string stored)
    {
        Assert.False(_hasher.Verify(stored, "plain-text-password"));
    }
}
