using System.Text;
using ClubHub.Api.Infrastructure.Redis;

namespace ClubHub.Api.Tests;

public sealed class RedisCacheSerializerTests
{
    private readonly RedisCacheSerializer _serializer = new();

    [Fact]
    public void RoundTripPreservesTypedPayload()
    {
        var payload = new CacheSample(42, "club");

        var serialized = _serializer.Serialize(payload);
        var result = _serializer.Deserialize<CacheSample>(serialized);

        Assert.Equal(RedisPayloadReadStatus.Success, result.Status);
        Assert.False(result.IsNull);
        Assert.Equal(payload, result.Value);
    }

    [Fact]
    public void RoundTripPreservesCachedNull()
    {
        var serialized = _serializer.Serialize<CacheSample>(null);

        var result = _serializer.Deserialize<CacheSample>(serialized);

        Assert.Equal(RedisPayloadReadStatus.Success, result.Status);
        Assert.True(result.IsNull);
        Assert.Null(result.Value);
    }

    [Fact]
    public void DeserializeRejectsUnknownEnvelopeVersion()
    {
        var serialized = Encoding.UTF8.GetBytes(
            """{"formatVersion":2,"isNull":false,"payload":{"id":42,"name":"club"}}""");

        var result = _serializer.Deserialize<CacheSample>(serialized);

        Assert.Equal(RedisPayloadReadStatus.UnsupportedVersion, result.Status);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"formatVersion":1,"isNull":false}""")]
    public void DeserializeRejectsMalformedPayload(string payload)
    {
        var result = _serializer.Deserialize<CacheSample>(Encoding.UTF8.GetBytes(payload));

        Assert.Equal(RedisPayloadReadStatus.InvalidPayload, result.Status);
    }

    private sealed record CacheSample(int Id, string Name);
}
