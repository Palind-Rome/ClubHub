using System.Text;
using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class CaptchaServiceTests
{
    [Fact]
    public void ChallengeRendersAnOpaqueImageWithoutTextNodes()
    {
        var service = CreateService("23456");

        var challenge = service.CreateChallenge();

        Assert.StartsWith("data:image/svg+xml;base64,", challenge.Image, StringComparison.Ordinal);
        var payload = challenge.Image["data:image/svg+xml;base64,".Length..];
        var svg = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        Assert.Contains("<svg ", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<text", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<title", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorrectCodeIsAcceptedOnlyOnce()
    {
        var service = CreateService("23456");
        var challenge = service.CreateChallenge();

        Assert.True(service.TryConsume(challenge.CaptchaToken, "23456"));
        Assert.False(service.TryConsume(challenge.CaptchaToken, "23456"));
    }

    [Fact]
    public void IncorrectCodeConsumesTheChallenge()
    {
        var service = CreateService("23456");
        var challenge = service.CreateChallenge();

        Assert.False(service.TryConsume(challenge.CaptchaToken, "99999"));
        Assert.False(service.TryConsume(challenge.CaptchaToken, "23456"));
    }

    [Fact]
    public void ExpiredCodeIsRejected()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = new CaptchaService(clock, () => "23456");
        var challenge = service.CreateChallenge();

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(service.TryConsume(challenge.CaptchaToken, "23456"));
    }

    private static CaptchaService CreateService(string code) =>
        new(new MutableTimeProvider(DateTimeOffset.UtcNow), () => code);

    private sealed class MutableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
