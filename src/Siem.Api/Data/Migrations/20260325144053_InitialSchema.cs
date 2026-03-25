using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siem.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    trace_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    severity_hint = table.Column<string>(type: "text", nullable: true),
                    model_id = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<double>(type: "double precision", nullable: true),
                    tool_name = table.Column<string>(type: "text", nullable: true),
                    tool_input = table.Column<string>(type: "text", nullable: true),
                    tool_output = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: false),
                    ingested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    kafka_partition = table.Column<short>(type: "smallint", nullable: true),
                    kafka_offset = table.Column<long>(type: "bigint", nullable: true),
                    source_sdk = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_event_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_count = table.Column<int>(type: "integer", nullable: false),
                    has_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    alert_count = table.Column<short>(type: "smallint", nullable: false),
                    max_severity = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_name = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    triggered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    assigned_to = table.Column<string>(type: "text", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    labels = table.Column<string>(type: "jsonb", nullable: false),
                    suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    suppressed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    suppression_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.alert_id);
                });

            migrationBuilder.CreateTable(
                name: "managed_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    condition_json = table.Column<string>(type: "jsonb", nullable: false),
                    evaluation_type = table.Column<string>(type: "text", nullable: false),
                    temporal_config = table.Column<string>(type: "jsonb", nullable: true),
                    sequence_config = table.Column<string>(type: "jsonb", nullable: true),
                    actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppressions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppressions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alert_events",
                columns: table => new
                {
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sequence_order = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_events", x => new { x.alert_id, x.event_id });
                    table.ForeignKey(
                        name: "FK_alert_events_alerts_alert_id",
                        column: x => x.alert_id,
                        principalTable: "alerts",
                        principalColumn: "alert_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "managed_list_members",
                columns: table => new
                {
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_list_members", x => new { x.list_id, x.value });
                    table.ForeignKey(
                        name: "FK_managed_list_members_managed_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "managed_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_events");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "alert_events");

            migrationBuilder.DropTable(
                name: "managed_list_members");

            migrationBuilder.DropTable(
                name: "rules");

            migrationBuilder.DropTable(
                name: "suppressions");

            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "managed_lists");
        }
    }
}
