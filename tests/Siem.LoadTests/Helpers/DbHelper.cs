using Npgsql;
using Siem.LoadTests.Fixtures;

namespace Siem.LoadTests.Helpers;

public static class DbHelper
{
    public static async Task TruncateAllTablesAsync()
    {
        await using var conn = new NpgsqlConnection(LoadTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            TRUNCATE alert_events, alerts, agent_sessions,
                     managed_list_members, managed_lists, suppressions, rules
            CASCADE;
            DELETE FROM agent_events;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task FlushRedisAsync()
    {
        var server = LoadTestFixture.RedisMultiplexer.GetServers()[0];
        await server.FlushDatabaseAsync();
    }
}
