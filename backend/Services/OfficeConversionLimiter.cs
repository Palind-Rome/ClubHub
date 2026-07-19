using Microsoft.Extensions.Options;

namespace ClubHub.Api.Services;

/// <summary>
/// 限制单个 API 进程内同时运行的 Office 转换数量。
/// </summary>
public sealed class OfficeConversionLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate;

    /// <summary>
    /// 使用预览配置创建进程级转换限流器。
    /// </summary>
    public OfficeConversionLimiter(IOptions<LearningPreviewOptions> options)
        : this(options.Value.MaxConcurrentConversions)
    {
    }

    internal OfficeConversionLimiter(int maxConcurrentConversions)
    {
        MaxConcurrency = Math.Clamp(maxConcurrentConversions, 1, 8);
        _gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    /// <summary>
    /// 实际采用的最大转换并发数。
    /// </summary>
    public int MaxConcurrency { get; }

    /// <summary>
    /// 在全局并发配额内执行一次转换操作。
    /// </summary>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 释放并发信号量。
    /// </summary>
    public void Dispose() => _gate.Dispose();
}
