using System.Diagnostics;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public sealed record DistributedLockPolicy(
    string Name,
    TimeSpan WaitTimeout,
    TimeSpan LeaseDuration,
    TimeSpan RetryInterval,
    TimeSpan? RenewalInterval = null)
{
    public void Validate()
    {
        if (!RedisKeyBuilder.IsValidNamespaceSegment(Name))
        {
            throw new ArgumentException(
                "Distributed lock policy names must be valid Redis namespace segments.",
                nameof(Name));
        }

        if (WaitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WaitTimeout),
                "Distributed lock wait timeout must not be negative.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LeaseDuration),
                "Distributed lock lease duration must be greater than zero.");
        }

        if (RetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryInterval),
                "Distributed lock retry interval must be greater than zero.");
        }

        if (RenewalInterval is not null &&
            (RenewalInterval <= TimeSpan.Zero || RenewalInterval >= LeaseDuration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RenewalInterval),
                "Distributed lock renewal interval must be positive and shorter than the lease.");
        }
    }
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    CancellationToken LeaseLost { get; }

    bool IsLeaseValid { get; }

    void ThrowIfLeaseLost();
}

public interface IDistributedLockService
{
    Task<IDistributedLockHandle?> TryAcquireAsync(
        RedisKey resource,
        DistributedLockPolicy policy,
        CancellationToken cancellationToken = default);

    Task<IDistributedLockHandle?> TryAcquireAsync(
        IReadOnlyCollection<RedisKey> resources,
        DistributedLockPolicy policy,
        CancellationToken cancellationToken = default);
}

public sealed class DistributedLockService : IDistributedLockService
{
    private readonly IRedisDatabase _database;
    private readonly RedisMetrics _metrics;
    private readonly ILogger<DistributedLockService> _logger;

    public DistributedLockService(
        IRedisDatabase database,
        RedisMetrics metrics,
        ILogger<DistributedLockService> logger)
    {
        _database = database;
        _metrics = metrics;
        _logger = logger;
    }

    public Task<IDistributedLockHandle?> TryAcquireAsync(
        RedisKey resource,
        DistributedLockPolicy policy,
        CancellationToken cancellationToken = default) =>
        TryAcquireAsync([resource], policy, cancellationToken);

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        IReadOnlyCollection<RedisKey> resources,
        DistributedLockPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var orderedResources = resources
            .Select(resource => resource.ToString())
            .Select(resource => string.IsNullOrWhiteSpace(resource)
                ? throw new ArgumentException(
                    "Distributed lock resources must not be empty.",
                    nameof(resources))
                : resource)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(resource => resource, StringComparer.Ordinal)
            .Select(resource => (RedisKey)resource)
            .ToArray();
        if (orderedResources.Length == 0)
        {
            throw new ArgumentException(
                "At least one distributed lock resource is required.",
                nameof(resources));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var owner = Guid.NewGuid().ToString("N");
        var elapsed = Stopwatch.StartNew();
        var attempted = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted = true;
            var acquired = new List<RedisKey>(orderedResources.Length);
            try
            {
                foreach (var resource in orderedResources)
                {
                    if (!await _database.StringSetIfNotExistsAsync(
                            resource,
                            owner,
                            policy.LeaseDuration,
                            cancellationToken))
                    {
                        break;
                    }

                    acquired.Add(resource);
                }
            }
            catch (Exception exception) when (IsRedisFailure(exception))
            {
                await ReleasePartialAsync(acquired, owner, policy.Name);
                _metrics.RecordLockAcquisition(
                    policy.Name,
                    "unavailable",
                    elapsed.Elapsed.TotalMilliseconds,
                    orderedResources.Length);
                _metrics.RecordFailure("distributed-lock-acquire");
                _logger.LogWarning(
                    exception,
                    "Redis distributed lock acquisition failed for policy {PolicyName}.",
                    policy.Name);
                throw new DistributedLockUnavailableException(
                    "The distributed lock service is unavailable.",
                    exception);
            }

            if (acquired.Count == orderedResources.Length)
            {
                _metrics.RecordLockAcquisition(
                    policy.Name,
                    "acquired",
                    elapsed.Elapsed.TotalMilliseconds,
                    orderedResources.Length);
                return new DistributedLockHandle(
                    _database,
                    _metrics,
                    _logger,
                    orderedResources,
                    owner,
                    policy);
            }

            if (!await ReleasePartialAsync(acquired, owner, policy.Name))
            {
                _metrics.RecordLockAcquisition(
                    policy.Name,
                    "cleanup-failed",
                    elapsed.Elapsed.TotalMilliseconds,
                    orderedResources.Length);
                throw new DistributedLockUnavailableException(
                    "A partial distributed lock acquisition could not be safely released.");
            }

            var remaining = policy.WaitTimeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                _metrics.RecordLockAcquisition(
                    policy.Name,
                    attempted ? "contended" : "timeout",
                    elapsed.Elapsed.TotalMilliseconds,
                    orderedResources.Length);
                return null;
            }

            var jitterMilliseconds = Random.Shared.Next(
                0,
                Math.Max(1, Math.Min(51, (int)Math.Ceiling(policy.RetryInterval.TotalMilliseconds / 2))));
            var delay = policy.RetryInterval + TimeSpan.FromMilliseconds(jitterMilliseconds);
            await Task.Delay(delay < remaining ? delay : remaining, cancellationToken);
        }
    }

    private async Task<bool> ReleasePartialAsync(
        IReadOnlyList<RedisKey> resources,
        RedisValue owner,
        string policyName)
    {
        var completed = true;
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                await _database.KeyDeleteIfValueMatchesAsync(
                    resources[index],
                    owner,
                    CancellationToken.None);
            }
            catch (Exception exception) when (IsRedisFailure(exception))
            {
                completed = false;
                _metrics.RecordFailure("distributed-lock-partial-release");
                _logger.LogWarning(
                    exception,
                    "Redis distributed lock partial release failed for policy {PolicyName}.",
                    policyName);
            }
        }

        return completed;
    }

    private static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;

    private sealed class DistributedLockHandle : IDistributedLockHandle
    {
        private const string RenewScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
              return redis.call('pexpire', KEYS[1], ARGV[2])
            end
            return 0
            """;

        private readonly IRedisDatabase _database;
        private readonly RedisMetrics _metrics;
        private readonly ILogger _logger;
        private readonly RedisKey[] _resources;
        private readonly RedisValue _owner;
        private readonly DistributedLockPolicy _policy;
        private readonly CancellationTokenSource _leaseLost = new();
        private readonly CancellationTokenSource _renewalStop = new();
        private readonly Stopwatch _held = Stopwatch.StartNew();
        private readonly Task _renewalTask;
        private int _disposed;

        public DistributedLockHandle(
            IRedisDatabase database,
            RedisMetrics metrics,
            ILogger logger,
            RedisKey[] resources,
            RedisValue owner,
            DistributedLockPolicy policy)
        {
            _database = database;
            _metrics = metrics;
            _logger = logger;
            _resources = resources;
            _owner = owner;
            _policy = policy;
            _renewalTask = policy.RenewalInterval is null
                ? Task.CompletedTask
                : MaintainLeaseAsync(policy.RenewalInterval.Value, _renewalStop.Token);
        }

        public CancellationToken LeaseLost => _leaseLost.Token;

        public bool IsLeaseValid =>
            Volatile.Read(ref _disposed) == 0 && !_leaseLost.IsCancellationRequested;

        public void ThrowIfLeaseLost()
        {
            if (!IsLeaseValid)
            {
                throw new DistributedLockLeaseLostException(
                    $"The distributed lock lease for policy '{_policy.Name}' is no longer valid.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _renewalStop.CancelAsync();
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException) when (_renewalStop.IsCancellationRequested)
            {
            }

            for (var index = _resources.Length - 1; index >= 0; index--)
            {
                try
                {
                    var released = await _database.KeyDeleteIfValueMatchesAsync(
                        _resources[index],
                        _owner,
                        CancellationToken.None);
                    _metrics.RecordLockRelease(
                        _policy.Name,
                        released ? "released" : "owner-mismatch");
                }
                catch (Exception exception) when (IsRedisFailure(exception))
                {
                    _metrics.RecordLockRelease(_policy.Name, "unavailable");
                    _metrics.RecordFailure("distributed-lock-release");
                    _logger.LogWarning(
                        exception,
                        "Redis distributed lock release failed for policy {PolicyName}.",
                        _policy.Name);
                }
            }

            _metrics.RecordLockHold(_policy.Name, _held.Elapsed.TotalMilliseconds);
            _renewalStop.Dispose();
            _leaseLost.Dispose();
        }

        private async Task MaintainLeaseAsync(
            TimeSpan renewalInterval,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    await Task.Delay(renewalInterval, cancellationToken);
                    foreach (var resource in _resources)
                    {
                        var renewed = await _database.ScriptEvaluateAsync(
                            RenewScript,
                            [resource],
                            [_owner, checked((long)Math.Ceiling(_policy.LeaseDuration.TotalMilliseconds))],
                            cancellationToken);
                        if ((long)renewed != 1)
                        {
                            MarkLeaseLost("owner-mismatch", null);
                            return;
                        }
                    }

                    _metrics.RecordLockRenewal(_policy.Name, "renewed");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (IsRedisFailure(exception))
                {
                    MarkLeaseLost("unavailable", exception);
                    return;
                }
            }
        }

        private void MarkLeaseLost(string outcome, Exception? exception)
        {
            if (_leaseLost.IsCancellationRequested)
            {
                return;
            }

            _metrics.RecordLockRenewal(_policy.Name, outcome);
            _metrics.RecordLockLeaseLost(_policy.Name, outcome);
            _metrics.RecordFailure("distributed-lock-renew");
            if (exception is null)
            {
                _logger.LogWarning(
                    "Redis distributed lock lease was lost for policy {PolicyName} because the owner changed.",
                    _policy.Name);
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "Redis distributed lock lease renewal failed for policy {PolicyName}.",
                    _policy.Name);
            }

            try
            {
                _leaseLost.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}

public sealed class DistributedLockUnavailableException : Exception
{
    public DistributedLockUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class DistributedLockLeaseLostException : Exception
{
    public DistributedLockLeaseLostException(string message)
        : base(message)
    {
    }
}
