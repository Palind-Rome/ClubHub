using System.Buffers;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.OpenAPITools.Models;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Idempotency;

public sealed partial class IdempotencyMiddleware
{
    private const int MaxResponseBytes = 64 * 1024;
    private const string RenewProcessingScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
          return redis.call('expire', KEYS[1], ARGV[2])
        end
        return 0
        """;
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ProcessingLifetime = TimeSpan.FromSeconds(60);
    private readonly RequestDelegate _next;
    private readonly RedisOptions _options;

    public IdempotencyMiddleware(RequestDelegate next, IOptions<RedisOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ClubHubDbContext db,
        IRedisDatabase redis,
        IRedisKeyBuilder keys,
        ILogger<IdempotencyMiddleware> logger)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentOperationAttribute>();
        if (metadata is null || !Enabled)
        {
            await _next(context);
            return;
        }

        if (!TryUserId(context.User, out var userId))
        {
            await WriteErrorAsync(context, 401, "登录状态已失效，请重新登录。");
            return;
        }

        var requestKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (!IsValidKey(requestKey))
        {
            await WriteErrorAsync(
                context,
                400,
                "Idempotency-Key 必须为 8–128 个安全 ASCII 字符。");
            return;
        }

        var requestHash = await BuildRequestHashAsync(context, metadata.OperationId);
        var keyHash = keys.HashSensitive(requestKey);
        var now = DateTime.UtcNow;
        var existing = await db.IdempotencyRecords
            .Where(record =>
                record.UserId == userId &&
                record.OperationScope == metadata.OperationId &&
                record.RequestKeyHash == keyHash)
            .SingleOrDefaultAsync(context.RequestAborted);
        if (existing is not null && existing.ExpiresAt > now)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(existing.RequestHash),
                    Encoding.ASCII.GetBytes(requestHash)))
            {
                await WriteErrorAsync(context, 409, "同一幂等 Key 不能用于不同请求。");
                return;
            }

            if (existing.RecordStatus == "succeeded")
            {
                await ReplayAsync(context, existing);
                return;
            }
        }

        var redisKey = keys.Build(
            "idempotency",
            "operation",
            metadata.OperationId,
            userId,
            keyHash);
        var owner = Guid.NewGuid().ToString("N");
        var processing = JsonSerializer.Serialize(new RedisIdempotencyState(
            "processing",
            requestHash,
            owner,
            null,
            null,
            null));
        try
        {
            var acquired = await redis.StringSetIfNotExistsAsync(
                redisKey,
                processing,
                ProcessingLifetime,
                context.RequestAborted);
            if (!acquired)
            {
                var currentValue = await redis.StringGetAsync(redisKey, context.RequestAborted);
                var current = currentValue.HasValue
                    ? JsonSerializer.Deserialize<RedisIdempotencyState>((string)currentValue!)
                    : null;
                if (current is not null && current.RequestHash != requestHash)
                {
                    await WriteErrorAsync(context, 409, "同一幂等 Key 不能用于不同请求。");
                    return;
                }
                if (current?.Status == "succeeded")
                {
                    await ReplayAsync(context, current);
                    return;
                }

                context.Response.Headers.RetryAfter = "2";
                await WriteErrorAsync(context, 409, "相同请求正在处理中，请稍后重试。");
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            logger.LogWarning(ex, "Redis idempotency acquisition failed for {OperationId}.", metadata.OperationId);
            await WriteErrorAsync(context, 503, "幂等服务暂不可用，请稍后重试。");
            return;
        }

        using var leaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var leaseTask = MaintainProcessingLeaseAsync(
            redis,
            redisKey,
            processing,
            logger,
            leaseCancellation.Token);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                context.RequestAborted);
        }
        catch
        {
            leaseCancellation.Cancel();
            await leaseTask;
            await SafeReleaseAsync(redis, redisKey, processing, logger, CancellationToken.None);
            throw;
        }
        await using (transaction)
        {
            var originalBody = context.Response.Body;
            await using var capture = new MemoryStream();
            context.Response.Body = capture;
            var transactionCompleted = false;
            try
            {
                await _next(context);
                capture.Position = 0;
                if (capture.Length > MaxResponseBytes)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    transactionCompleted = true;
                    await SafeReleaseAsync(redis, redisKey, processing, logger, context.RequestAborted);
                    capture.SetLength(0);
                    context.Response.Clear();
                    await WriteErrorAsync(
                        context,
                        503,
                        "响应超过幂等重放上限，操作已回滚，请缩小请求后重试。");
                    capture.Position = 0;
                    context.Response.Body = originalBody;
                    await capture.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                var bodyBytes = capture.ToArray();
                if (context.Response.StatusCode is >= 200 and < 300)
                {
                    var body = Encoding.UTF8.GetString(bodyBytes);
                    var headers = CaptureHeaders(context.Response);
                    now = DateTime.UtcNow;
                    var record = existing ?? new IdempotencyRecord
                    {
                        UserId = userId,
                        OperationScope = metadata.OperationId,
                        RequestKeyHash = keyHash,
                        RequestHash = requestHash,
                        CreatedAt = now
                    };
                    record.RecordStatus = "succeeded";
                    record.HttpStatus = context.Response.StatusCode;
                    record.ContentType = context.Response.ContentType;
                    record.ResponseHeaders = JsonSerializer.Serialize(headers);
                    record.ResponseBody = body;
                    record.ExpiresAt = now.Add(ResultLifetime);
                    record.UpdatedAt = now;
                    if (existing is null) db.IdempotencyRecords.Add(record);
                    await db.SaveChangesAsync(context.RequestAborted);
                    await transaction.CommitAsync(context.RequestAborted);
                    transactionCompleted = true;

                    var succeeded = new RedisIdempotencyState(
                        "succeeded",
                        requestHash,
                        owner,
                        context.Response.StatusCode,
                        body,
                        headers);
                    try
                    {
                        await redis.StringSetAsync(
                            redisKey,
                            JsonSerializer.Serialize(succeeded),
                            ResultLifetime,
                            context.RequestAborted);
                    }
                    catch (Exception ex) when (ex is RedisException or TimeoutException)
                    {
                        logger.LogWarning(
                            ex,
                            "Redis idempotency result write failed; Oracle ledger will replay {OperationId}.",
                            metadata.OperationId);
                    }
                }
                else
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    transactionCompleted = true;
                    await SafeReleaseAsync(redis, redisKey, processing, logger, context.RequestAborted);
                }

                context.Response.Body = originalBody;
                capture.Position = 0;
                await capture.CopyToAsync(originalBody, context.RequestAborted);
            }
            catch
            {
                if (!transactionCompleted)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                await SafeReleaseAsync(redis, redisKey, processing, logger, CancellationToken.None);
                throw;
            }
            finally
            {
                leaseCancellation.Cancel();
                await leaseTask;
                context.Response.Body = originalBody;
            }
        }
    }

    private bool Enabled => _options.Enabled && _options.Features.Idempotency;

    private static async Task<string> BuildRequestHashAsync(HttpContext context, string operationId)
    {
        context.Request.EnableBuffering();
        using var bodyHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        string bodyDigest;
        try
        {
            int read;
            while ((read = await context.Request.Body.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       context.RequestAborted)) > 0)
            {
                bodyHash.AppendData(buffer, 0, read);
            }
            bodyDigest = Convert.ToHexStringLower(bodyHash.GetHashAndReset());
        }
        finally
        {
            context.Request.Body.Position = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
        var canonical = string.Join(
            '\n',
            context.Request.Method,
            operationId,
            context.Request.Path.Value,
            context.Request.QueryString.Value,
            bodyDigest);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task MaintainProcessingLeaseAsync(
        IRedisDatabase redis,
        RedisKey redisKey,
        string processing,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var renewed = await redis.ScriptEvaluateAsync(
                    RenewProcessingScript,
                    [redisKey],
                    [processing, (long)ProcessingLifetime.TotalSeconds],
                    cancellationToken);
                if ((long)renewed != 1) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal request completion.
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(
                ex,
                "Unable to renew Redis idempotency processing lease; Oracle ledger remains authoritative.");
        }
    }

    private static Dictionary<string, string> CaptureHeaders(HttpResponse response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Content-Type", "Location" })
        {
            if (response.Headers.TryGetValue(name, out var value)) result[name] = value.ToString();
        }
        return result;
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        var headers = string.IsNullOrWhiteSpace(record.ResponseHeaders)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(record.ResponseHeaders);
        await ReplayAsync(
            context,
            new RedisIdempotencyState(
                "succeeded",
                record.RequestHash,
                string.Empty,
                record.HttpStatus,
                record.ResponseBody,
                headers));
    }

    private static async Task ReplayAsync(HttpContext context, RedisIdempotencyState state)
    {
        context.Response.StatusCode = state.HttpStatus ?? 200;
        if (state.Headers is not null)
        {
            foreach (var header in state.Headers) context.Response.Headers[header.Key] = header.Value;
        }
        context.Response.Headers["Idempotency-Replayed"] = "true";
        if (!string.IsNullOrEmpty(state.Body)) await context.Response.WriteAsync(state.Body);
    }

    private static async Task WriteErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ApiError { Message = message });
    }

    private static async Task SafeReleaseAsync(
        IRedisDatabase redis,
        RedisKey redisKey,
        string processing,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await redis.KeyDeleteIfValueMatchesAsync(
                redisKey,
                processing,
                cancellationToken);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Unable to release Redis idempotency processing marker.");
        }
    }

    private static bool TryUserId(ClaimsPrincipal principal, out int userId) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private static bool IsValidKey(string value) =>
        value.Length is >= 8 and <= 128 && SafeKeyPattern().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9._~:+/\-=]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKeyPattern();

    private sealed record RedisIdempotencyState(
        string Status,
        string RequestHash,
        string Owner,
        int? HttpStatus,
        string? Body,
        Dictionary<string, string>? Headers);
}
