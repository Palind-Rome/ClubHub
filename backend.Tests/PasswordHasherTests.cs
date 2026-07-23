using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashCanBeVerifiedOnlyWithOriginalPassword()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(PasswordHasher.Verify("wrong password", hash));
    }

    [Fact]
    public void HashUsesRandomSalt()
    {
        var first = PasswordHasher.Hash("same password");
        var second = PasswordHasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("same password", first));
        Assert.True(PasswordHasher.Verify("same password", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("PBKDF2$not-a-number$salt$hash")]
    [InlineData("PBKDF2$600000$not-base64$not-base64")]
    public void VerifyRejectsMalformedStoredHashes(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("password", storedHash));
    }
}
