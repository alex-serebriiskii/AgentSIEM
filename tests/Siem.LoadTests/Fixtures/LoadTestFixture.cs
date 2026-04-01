using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siem.Api.Data;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Siem.LoadTests.Fixtures;

/// <summary>
/// Assembly-level fixture that starts TimescaleDB, Redis, and Kafka containers once
/// for the entire load test run.
/// </summary>
public static class LoadTestFixture
{
    private static PostgreSqlContainer? _timescaleDb;
    private static RedisContainer? _redis;
    private static KafkaContainer? _kafka;
    private static IConnectionMultiplexer? _redisMultiplexer;

    public static string TimescaleConnectionString =>
        _timescaleDb?.GetConnectionString()
        ?? throw new InvalidOperationException("TimescaleDB container not started");

    public static IConnectionMultiplexer RedisMultiplexer =>
        _redisMultiplexer
        ?? throw new InvalidOperationException("Redis not connected");

    public static string RedisConnectionString =>
        _redis?.GetConnectionString()
        ?? throw new InvalidOperationException("Redis container not started");

    public static string KafkaBootstrapServers =>
        _kafka?.GetBootstrapAddress()
        ?? throw new InvalidOperationException("Kafka container not started");

    [Before(Assembly)]
    public static async Task StartContainers()
    {
        _timescaleDb = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb:latest-pg16")
            .WithDatabase("agentsiem")
            .WithUsername("siem")
            .WithPassword("siem")
            .Build();

        _redis = new RedisBuilder()
            .Build();

        _kafka = new KafkaBuilder()
            .Build();

        await Task.WhenAll(
            _timescaleDb.StartAsync(),
            _redis.StartAsync(),
            _kafka.StartAsync());

        var options = new DbContextOptionsBuilder<SiemDbContext>()
            .UseNpgsql(TimescaleConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new SiemDbContext(options);
        await context.Database.MigrateAsync();

        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(
            _redis.GetConnectionString() + ",allowAdmin=true");
    }

    [After(Assembly)]
    public static async Task StopContainers()
    {
        if (_redisMultiplexer is not null)
            await _redisMultiplexer.DisposeAsync();

        var tasks = new List<Task>();
        if (_timescaleDb is not null) tasks.Add(_timescaleDb.DisposeAsync().AsTask());
        if (_redis is not null) tasks.Add(_redis.DisposeAsync().AsTask());
        if (_kafka is not null) tasks.Add(_kafka.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
    }

    public static SiemDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SiemDbContext>()
            .UseNpgsql(TimescaleConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new SiemDbContext(options);
    }
}
