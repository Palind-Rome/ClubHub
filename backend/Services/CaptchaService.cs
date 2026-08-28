using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Services;

public sealed class CaptchaService
{
    private const string CodeAlphabet = "23456789";
    private const int CodeLength = 5;
    private const int MaxActiveChallenges = 10_000;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
            ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
            ['4'] = ["10010", "10010", "10010", "11111", "00010", "00010", "00010"],
            ['5'] = ["11111", "10000", "10000", "11110", "00001", "00001", "11110"],
            ['6'] = ["01110", "10001", "10000", "11110", "10001", "10001", "01110"],
            ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
            ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
            ['9'] = ["01110", "10001", "10001", "01111", "00001", "10001", "01110"]
        };

    private readonly ConcurrentDictionary<string, CaptchaEntry> _challenges = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _codeFactory;

    public CaptchaService(TimeProvider timeProvider)
        : this(timeProvider, CreateCode)
    {
    }

    internal CaptchaService(TimeProvider timeProvider, Func<string> codeFactory)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _codeFactory = codeFactory ?? throw new ArgumentNullException(nameof(codeFactory));
    }

    public CaptchaChallenge CreateChallenge()
    {
        var now = _timeProvider.GetUtcNow();
        RemoveExpired(now);

        var token = CreateToken();
        var code = _codeFactory();
        if (!IsValidCode(code))
        {
            throw new InvalidOperationException("Captcha code factory returned an invalid code.");
        }

        var expiresAt = now.Add(ChallengeLifetime);
        _challenges[token] = new CaptchaEntry(code, expiresAt);
        TrimOverflow();

        return new CaptchaChallenge
        {
            CaptchaToken = token,
            Image = RenderImage(code),
            ExpiresAt = expiresAt
        };
    }

    public bool TryConsume(string? token, string? code)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
        {
            return false;
        }

        if (!_challenges.TryRemove(token.Trim(), out var challenge))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() > challenge.ExpiresAt)
        {
            return false;
        }

        var normalizedCode = code?.Trim().ToUpperInvariant();
        if (normalizedCode is null || normalizedCode.Length != CodeLength)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(challenge.Code);
        var actual = Encoding.UTF8.GetBytes(normalizedCode);
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string CreateCode()
    {
        var builder = new StringBuilder(CodeLength);
        for (var index = 0; index < CodeLength; index++)
        {
            builder.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
        }

        return builder.ToString();
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsValidCode(string? code) =>
        code is not null &&
        code.Length == CodeLength &&
        code.All(character => CodeAlphabet.Contains(character, StringComparison.Ordinal));

    private static string RenderImage(string code)
    {
        const int width = 192;
        const int height = 64;
        const int blockWidth = 3;
        const int blockHeight = 4;
        const int characterStartX = 20;
        const int characterStep = 34;
        const int characterStartY = 14;
        var builder = new StringBuilder(4_000);

        builder.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.Append("<rect width=\"100%\" height=\"100%\" rx=\"8\" fill=\"#eef5ff\"/>");

        for (var index = 0; index < 12; index++)
        {
            var x1 = RandomNumberGenerator.GetInt32(width);
            var y1 = RandomNumberGenerator.GetInt32(height);
            var x2 = RandomNumberGenerator.GetInt32(width);
            var y2 = RandomNumberGenerator.GetInt32(height);
            var color = index % 2 == 0 ? "#9ab4d5" : "#c5a7c5";
            builder.Append($"<path d=\"M{x1} {y1}L{x2} {y2}\" stroke=\"{color}\" stroke-width=\"{RandomNumberGenerator.GetInt32(1, 3)}\" opacity=\".65\"/>");
        }

        for (var index = 0; index < 20; index++)
        {
            var x = RandomNumberGenerator.GetInt32(width);
            var y = RandomNumberGenerator.GetInt32(height);
            var radius = RandomNumberGenerator.GetInt32(1, 4);
            builder.Append($"<circle cx=\"{x}\" cy=\"{y}\" r=\"{radius}\" fill=\"#7f9bbd\" opacity=\".55\"/>");
        }

        for (var index = 0; index < code.Length; index++)
        {
            var x = characterStartX + index * characterStep;
            var rotation = RandomNumberGenerator.GetInt32(-11, 12);
            var pattern = Glyphs[code[index]];
            builder.Append($"<g transform=\"rotate({rotation} {x + 8} 32)\" fill=\"#244b78\">");
            for (var row = 0; row < pattern.Length; row++)
            {
                for (var column = 0; column < pattern[row].Length; column++)
                {
                    if (pattern[row][column] != '1') continue;

                    var blockX = x + column * blockWidth;
                    var blockY = characterStartY + row * (blockHeight + 1);
                    builder.Append($"<rect x=\"{blockX}\" y=\"{blockY}\" width=\"{blockWidth}\" height=\"{blockHeight}\" rx=\"1\"/>");
                }
            }

            builder.Append("</g>");
        }

        builder.Append("</svg>");
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(builder.ToString()))}";
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var pair in _challenges)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _challenges.TryRemove(pair.Key, out _);
            }
        }
    }

    private void TrimOverflow()
    {
        while (_challenges.Count > MaxActiveChallenges)
        {
            var oldest = _challenges.OrderBy(pair => pair.Value.ExpiresAt).FirstOrDefault();
            if (oldest.Key is null)
            {
                return;
            }

            _challenges.TryRemove(oldest.Key, out _);
        }
    }

    private sealed record CaptchaEntry(string Code, DateTimeOffset ExpiresAt);
}
