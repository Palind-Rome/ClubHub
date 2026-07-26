using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public interface IRedisDatabase
{
    Task<RedisValue> StringGetAsync(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<bool> StringSetAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<bool> StringSetIfNotExistsAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<bool> KeyDeleteAsync(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<bool> KeyDeleteIfValueMatchesAsync(
        RedisKey key,
        RedisValue expectedValue,
        CancellationToken cancellationToken = default);

    Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default);
}

internal sealed class StackExchangeRedisDatabase : IRedisDatabase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisOptions _options;

    public StackExchangeRedisDatabase(
        IServiceProvider serviceProvider,
        Microsoft.Extensions.Options.IOptions<RedisOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public async Task<RedisValue> StringGetAsync(
        RedisKey key,
        CancellationToken cancellationToken = default) =>
        await GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken);

    public async Task<bool> StringSetAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default) =>
        await GetDatabase()
            .StringSetAsync(key, value, expiration)
            .WaitAsync(cancellationToken);

    public async Task<bool> StringSetIfNotExistsAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default) =>
        await GetDatabase()
            .StringSetAsync(key, value, expiration, When.NotExists)
            .WaitAsync(cancellationToken);

    public async Task<bool> KeyDeleteAsync(
        RedisKey key,
        CancellationToken cancellationToken = default) =>
        await GetDatabase().KeyDeleteAsync(key).WaitAsync(cancellationToken);

    public async Task<bool> KeyDeleteIfValueMatchesAsync(
        RedisKey key,
        RedisValue expectedValue,
        CancellationToken cancellationToken = default)
    {
        const string script =
            "if redis.call('get', KEYS[1]) == ARGV[1] then " +
            "return redis.call('del', KEYS[1]) else return 0 end";
        var result = await GetDatabase()
            .ScriptEvaluateAsync(
                script,
                [key],
                [expectedValue])
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task<TimeSpan> PingAsync(
        CancellationToken cancellationToken = default) =>
        await GetDatabase().PingAsync().WaitAsync(cancellationToken);

    private IDatabase GetDatabase()
    {
        var connection = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        return connection.GetDatabase(_options.Database);
    }
}
