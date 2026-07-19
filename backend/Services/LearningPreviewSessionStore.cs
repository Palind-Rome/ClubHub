using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ClubHub.Api.Services;

/// <summary>
/// 在预览 Cookie 的短时生命周期内保存已经解析和校验过的预览元数据。
/// </summary>
public sealed class LearningPreviewSessionStore : IDisposable
{
    private const int MaxSessions = 4096;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxSessions });

    /// <summary>
    /// 保存与预览令牌、用户和资源绑定的预览元数据。
    /// </summary>
    public void Store(
        string token,
        int userId,
        int itemId,
        PreparedLearningPreview preview,
        TimeSpan lifetime)
    {
        _cache.Set(
            BuildKey(token),
            new LearningPreviewSession(userId, itemId, preview),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime,
                Size = 1
            });
    }

    /// <summary>
    /// 读取与当前令牌、用户和资源完全匹配的预览元数据。
    /// </summary>
    public bool TryGet(
        string token,
        int userId,
        int itemId,
        out PreparedLearningPreview? preview)
    {
        preview = null;
        if (!_cache.TryGetValue<LearningPreviewSession>(BuildKey(token), out var session) ||
            session is null || session.UserId != userId || session.ItemId != itemId)
        {
            return false;
        }

        preview = session.Preview;
        return true;
    }

    /// <summary>
    /// 释放预览会话缓存。
    /// </summary>
    public void Dispose() => _cache.Dispose();

    private static string BuildKey(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record LearningPreviewSession(
        int UserId,
        int ItemId,
        PreparedLearningPreview Preview);
}
