using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubHub.Api.Infrastructure.Redis;

public enum RedisPayloadReadStatus
{
    Success,
    UnsupportedVersion,
    InvalidPayload
}

public readonly record struct RedisPayloadReadResult<T>(
    RedisPayloadReadStatus Status,
    T? Value,
    bool IsNull);

public interface IRedisCacheSerializer
{
    byte[] Serialize<T>(T? value);

    RedisPayloadReadResult<T> Deserialize<T>(ReadOnlySpan<byte> payload);
}

public sealed class RedisCacheSerializer : IRedisCacheSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public byte[] Serialize<T>(T? value)
    {
        var envelope = new RedisCacheEnvelope<T>(
            CurrentVersion,
            value is null,
            value);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
    }

    public RedisPayloadReadResult<T> Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<RedisCacheEnvelope<T>>(
                payload,
                SerializerOptions);
            if (envelope is null)
            {
                return new(RedisPayloadReadStatus.InvalidPayload, default, false);
            }

            if (envelope.FormatVersion != CurrentVersion)
            {
                return new(RedisPayloadReadStatus.UnsupportedVersion, default, false);
            }

            if (envelope.IsNull)
            {
                return new(RedisPayloadReadStatus.Success, default, true);
            }

            if (envelope.Payload is null)
            {
                return new(RedisPayloadReadStatus.InvalidPayload, default, false);
            }

            return new(RedisPayloadReadStatus.Success, envelope.Payload, false);
        }
        catch (JsonException)
        {
            return new(RedisPayloadReadStatus.InvalidPayload, default, false);
        }
        catch (NotSupportedException)
        {
            return new(RedisPayloadReadStatus.InvalidPayload, default, false);
        }
    }

    private sealed record RedisCacheEnvelope<T>(
        int FormatVersion,
        bool IsNull,
        T? Payload);
}
