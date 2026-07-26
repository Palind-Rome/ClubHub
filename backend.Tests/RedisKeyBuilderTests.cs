using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Tests;

public sealed class RedisKeyBuilderTests
{
    [Fact]
    public void BuildUsesVersionedApplicationAndEnvironmentNamespace()
    {
        var builder = CreateBuilder();

        var key = builder.Build("activity", "detail", 42);

        Assert.Equal("clubhub:test:activity:detail:v1:42", key.ToString());
    }

    [Fact]
    public void BuildEscapesIdentityDelimiters()
    {
        var builder = CreateBuilder();

        var key = builder.Build("auth", "session", "student:42");

        Assert.Equal("clubhub:test:auth:session:v1:student%3A42", key.ToString());
    }

    [Fact]
    public void BuildRejectsUnscopedOrOversizedKeys()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.Build("Activity", "detail", 42));
        Assert.Throws<ArgumentException>(() => builder.Build("activity", "detail"));
        Assert.Throws<ArgumentException>(
            () => builder.Build("activity", "detail", new string('x', 201)));
    }

    [Fact]
    public void HashSensitiveReturnsStableSha256WithoutOriginalValue()
    {
        var builder = CreateBuilder();

        var first = builder.HashSensitive("sensitive-token");
        var second = builder.HashSensitive("sensitive-token");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("sensitive-token", first, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    private static RedisKeyBuilder CreateBuilder() =>
        new(Options.Create(new RedisOptions { EnvironmentPrefix = "test" }));
}
