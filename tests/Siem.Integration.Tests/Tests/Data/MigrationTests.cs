using FluentAssertions;
using Npgsql;
using Siem.Integration.Tests.Fixtures;

namespace Siem.Integration.Tests.Tests.Data;

[NotInParallel("database")]
public class MigrationTests
{
    [Test]
    public async Task HypertableExists_AfterMigration()
    {
        await using var conn = new NpgsqlConnection(
            IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM timescaledb_information.hypertables
            WHERE hypertable_name = 'agent_events'
            """;
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    [Test]
    public async Task GetSessionTimeline_FunctionExists()
    {
        await using var conn = new NpgsqlConnection(
            IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.routines
            WHERE routine_name = 'get_session_timeline'
            """;
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    [Test]
    public async Task AllExpectedTables_ExistAfterMigration()
    {
        var expectedTables = new[]
        {
            "rules", "alerts", "alert_events", "agent_sessions",
            "managed_lists", "managed_list_members", "suppressions", "agent_events"
        };

        await using var conn = new NpgsqlConnection(
            IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();

        foreach (var table in expectedTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_name = '{table}' AND table_schema = 'public'
                """;
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(1, $"table '{table}' should exist");
        }
    }
}
