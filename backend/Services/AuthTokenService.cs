using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

public sealed class AuthTokenService
{
    private const string PreviewTokenPrefix = "preview";
    private const string LocalDevelopmentSigningKey = "ClubHub.LocalDevelopment.TokenSigningKey.ChangeForProduction";
    private readonly byte[] _signingKey;
    private readonly int _previewSessionLifetimeMinutes;
    private readonly int _tokenLifetimeHours;

    public const string PreviewCookieName = "clubhub-preview";

    public TimeSpan PreviewSessionLifetime => TimeSpan.FromMinutes(_previewSessionLifetimeMinutes);

    public AuthTokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredKey = configuration["Authentication:TokenSigningKey"];
        var signingKey = configuredKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Authentication:TokenSigningKey must be configured outside Development.");
            }

            signingKey = LocalDevelopmentSigningKey;
        }

        _signingKey = Encoding.UTF8.GetBytes(signingKey);
        _previewSessionLifetimeMinutes = Math.Clamp(
            configuration.GetValue<int?>("LearningPreview:SessionLifetimeMinutes") ?? 30,
            1,
            120);
        _tokenLifetimeHours = Math.Clamp(
            configuration.GetValue<int?>("Authentication:Sessions:AbsoluteLifetimeHours") ?? 12,
            1,
            168);
    }

    public string CreateToken(User user)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new AuthTokenPayload(
            user.UserId,
            user.Username,
            Guid.NewGuid().ToString("N"),
            issuedAt.ToUnixTimeSeconds(),
            issuedAt.AddHours(_tokenLifetimeHours).ToUnixTimeSeconds());
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signaturePart = Sign(payloadPart);

        return $"{payloadPart}.{signaturePart}";
    }

    public bool TryValidateToken(string token, out AuthTokenPrincipal principal)
    {
        principal = default;

        var parts = token.Split('.', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        var expectedSignature = Sign(parts[0]);
        if (!FixedTimeEquals(parts[1], expectedSignature))
        {
            return false;
        }

        AuthTokenPayload? payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            payload = JsonSerializer.Deserialize<AuthTokenPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload is null || payload.UserId <= 0)
        {
            return false;
        }

        if (payload.ExpiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.SessionId) ||
            payload.IssuedAtUnix <= 0 ||
            payload.IssuedAtUnix >= payload.ExpiresAtUnix)
        {
            return false;
        }

        principal = new AuthTokenPrincipal(
            payload.UserId,
            payload.Username,
            payload.SessionId,
            DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnix),
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix));
        return true;
    }

    public bool TryValidateLegacyToken(string token, out AuthTokenPrincipal principal)
    {
        principal = default;
        var parts = token.Split('.', 2);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !FixedTimeEquals(parts[1], Sign(parts[0])))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            var root = document.RootElement;
            if (root.TryGetProperty(nameof(AuthTokenPayload.SessionId), out _) ||
                !root.TryGetProperty(nameof(AuthTokenPayload.UserId), out var userIdElement) ||
                !root.TryGetProperty(nameof(AuthTokenPayload.ExpiresAtUnix), out var expiresElement) ||
                !userIdElement.TryGetInt32(out var userId) ||
                !expiresElement.TryGetInt64(out var expiresAtUnix) ||
                userId <= 0 ||
                expiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return false;
            }

            var username = root.TryGetProperty(nameof(AuthTokenPayload.Username), out var usernameElement) &&
                           usernameElement.ValueKind == JsonValueKind.String
                ? usernameElement.GetString()
                : null;
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
            principal = new AuthTokenPrincipal(
                userId,
                username,
                "legacy",
                expiresAt.AddHours(-_tokenLifetimeHours),
                expiresAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public string CreatePreviewToken(int userId, int itemId)
    {
        var payload = new PreviewTokenPayload(
            userId,
            itemId,
            DateTimeOffset.UtcNow.AddMinutes(_previewSessionLifetimeMinutes).ToUnixTimeSeconds());
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signedPart = $"{PreviewTokenPrefix}.{payloadPart}";
        return $"{signedPart}.{Sign(signedPart)}";
    }

    public bool TryValidatePreviewToken(
        string token,
        int expectedItemId,
        out AuthTokenPrincipal principal)
    {
        principal = default;
        var parts = token.Split('.', 3);
        if (parts.Length != 3 || parts[0] != PreviewTokenPrefix ||
            string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        var signedPart = $"{parts[0]}.{parts[1]}";
        if (!FixedTimeEquals(parts[2], Sign(signedPart))) return false;

        PreviewTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PreviewTokenPayload>(
                Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload is null || payload.UserId <= 0 || payload.ItemId != expectedItemId ||
            payload.ExpiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        principal = new AuthTokenPrincipal(
            payload.UserId,
            null,
            $"preview-{payload.ItemId}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix));
        return true;
    }

    private string Sign(string payloadPart)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadPart)));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private sealed record AuthTokenPayload(
        int UserId,
        string? Username,
        string SessionId,
        long IssuedAtUnix,
        long ExpiresAtUnix);

    private sealed record PreviewTokenPayload(int UserId, int ItemId, long ExpiresAtUnix);
}

public readonly record struct AuthTokenPrincipal(
    int UserId,
    string? Username,
    string SessionId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
