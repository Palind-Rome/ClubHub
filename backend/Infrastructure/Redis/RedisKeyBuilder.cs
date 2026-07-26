using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public interface IRedisKeyBuilder
{
    RedisKey Build(string module, string purpose, params object[] identitySegments);

    string HashSensitive(string value);
}

public sealed partial class RedisKeyBuilder : IRedisKeyBuilder
{
    private const string ApplicationPrefix = "clubhub";
    private const string PayloadVersion = "v1";
    private const int MaxKeyBytes = 200;
    private readonly string _environmentPrefix;

    public RedisKeyBuilder(IOptions<RedisOptions> options)
    {
        _environmentPrefix = options.Value.EnvironmentPrefix;

        if (!IsValidNamespaceSegment(_environmentPrefix))
        {
            throw new OptionsValidationException(
                RedisOptions.SectionName,
                typeof(RedisOptions),
                ["Redis:EnvironmentPrefix is not a valid key namespace segment."]);
        }
    }

    public RedisKey Build(string module, string purpose, params object[] identitySegments)
    {
        ArgumentNullException.ThrowIfNull(identitySegments);
        ValidateNamespaceSegment(module, nameof(module));
        ValidateNamespaceSegment(purpose, nameof(purpose));

        if (identitySegments.Length == 0)
        {
            throw new ArgumentException(
                "At least one identity segment is required.",
                nameof(identitySegments));
        }

        var encodedIdentity = identitySegments.Select(EncodeIdentitySegment);
        var key = string.Join(
            ':',
            [ApplicationPrefix, _environmentPrefix, module, purpose, PayloadVersion, .. encodedIdentity]);

        if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
        {
            throw new ArgumentException(
                $"Redis key must not exceed {MaxKeyBytes} UTF-8 bytes.",
                nameof(identitySegments));
        }

        return key;
    }

    public string HashSensitive(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest);
    }

    internal static bool IsValidNamespaceSegment(string? value) =>
        value is not null && NamespaceSegmentPattern().IsMatch(value);

    private static void ValidateNamespaceSegment(string value, string parameterName)
    {
        if (!IsValidNamespaceSegment(value))
        {
            throw new ArgumentException(
                "Namespace segments must contain only lowercase letters, digits, and hyphens.",
                parameterName);
        }
    }

    private static string EncodeIdentitySegment(object segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var value = Convert.ToString(segment, System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Redis key identity segments must not be empty.");
        }

        return Uri.EscapeDataString(value);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceSegmentPattern();
}
