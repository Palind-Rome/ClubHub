using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

public sealed class AuthTokenService
{
    private const int TokenLifetimeHours = 12;
    private const string LocalDevelopmentSigningKey = "ClubHub.LocalDevelopment.TokenSigningKey.ChangeForProduction";
    private readonly byte[] _signingKey;

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
    }

    public string CreateToken(User user)
    {
        var payload = new AuthTokenPayload(
            user.UserId,
            user.Username,
            DateTimeOffset.UtcNow.AddHours(TokenLifetimeHours).ToUnixTimeSeconds());
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

        principal = new AuthTokenPrincipal(payload.UserId, payload.Username);
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

    private sealed record AuthTokenPayload(int UserId, string? Username, long ExpiresAtUnix);
}

public readonly record struct AuthTokenPrincipal(int UserId, string? Username);
