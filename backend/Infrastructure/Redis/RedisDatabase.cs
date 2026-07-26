using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public interface IRedisDatabase
{
    Task<RedisValue> StringGetAsync(RedisKey key);

    Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan expiration);

    Task<bool> KeyDeleteAsync(RedisKey key);

    Task<TimeSpan> PingAsync();
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

    public Task<RedisValue> StringGetAsync(RedisKey key) =>
        GetDatabase().StringGetAsync(key);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan expiration) =>
        GetDatabase().StringSetAsync(key, value, expiration);

    public Task<bool> KeyDeleteAsync(RedisKey key) =>
        GetDatabase().KeyDeleteAsync(key);

    public Task<TimeSpan> PingAsync() =>
        GetDatabase().PingAsync();

    private IDatabase GetDatabase()
    {
        var connection = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        return connection.GetDatabase(_options.Database);
    }
}
