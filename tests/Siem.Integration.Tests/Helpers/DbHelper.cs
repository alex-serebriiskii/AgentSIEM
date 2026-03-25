using Npgsql;
using Siem.Integration.Tests.Fixtures;

namespace Siem.Integration.Tests.Helpers;

public static class DbHelper
{
    public static async Task TruncateAllTablesAsync()
    {
        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
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
        var server = IntegrationTestFixture.RedisMultiplexer.GetServers()[0];
        await server.FlushDatabaseAsync();
    }
}
