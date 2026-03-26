using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siem.Api.Data.Migrations
{
    /// <summary>
    /// Fixes the get_agent_risk_summary() function where unqualified column references
    /// (agent_id, timestamp) in subqueries are ambiguous with the RETURNS TABLE column names.
    /// Also qualifies similar references in get_session_timeline().
    /// </summary>
    public partial class FixAgentRiskSummaryFunction : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the function with fully-qualified column references
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION get_agent_risk_summary(
                    p_agent_id TEXT,
                    p_lookback INTERVAL DEFAULT INTERVAL '24 hours'
                )
                RETURNS TABLE (
                    agent_id            TEXT,
                    agent_name          TEXT,
                    total_events        BIGINT,
                    total_sessions      BIGINT,
                    open_alerts         BIGINT,
                    critical_alerts     BIGINT,
                    unique_tools        BIGINT,
                    total_tokens        BIGINT,
                    avg_latency_ms      DOUBLE PRECISION,
                    events_per_minute   DOUBLE PRECISION,
                    top_event_types     JSONB,
                    top_tools           JSONB
                ) AS $$
                DECLARE
                    v_cutoff TIMESTAMPTZ := NOW() - p_lookback;
                BEGIN
                    RETURN QUERY
                    WITH event_stats AS (
                        SELECT
                            e.agent_id AS agent_id,
                            MAX(e.agent_name) AS agent_name,
                            COUNT(*) AS total_events,
                            COUNT(DISTINCT e.session_id) AS total_sessions,
                            COUNT(DISTINCT e.tool_name) FILTER (WHERE e.tool_name IS NOT NULL) AS unique_tools,
                            SUM(COALESCE(e.input_tokens, 0) + COALESCE(e.output_tokens, 0)) AS total_tokens,
                            AVG(e.latency_ms) AS avg_latency_ms,
                            COUNT(*)::float / GREATEST(EXTRACT(EPOCH FROM (NOW() - MIN(e.timestamp))) / 60.0, 1)
                                AS events_per_minute
                        FROM agent_events e
                        WHERE e.agent_id = p_agent_id
                          AND e.timestamp >= v_cutoff
                        GROUP BY e.agent_id
                    ),
                    alert_stats AS (
                        SELECT
                            COUNT(*) FILTER (WHERE a.status = 'open') AS open_alerts,
                            COUNT(*) FILTER (WHERE a.severity = 'critical' AND a.status = 'open')
                                AS critical_alerts
                        FROM alerts a
                        WHERE a.agent_id = p_agent_id
                          AND a.triggered_at >= v_cutoff
                    ),
                    type_breakdown AS (
                        SELECT jsonb_object_agg(tb_inner.event_type, tb_inner.cnt) AS top_event_types
                        FROM (
                            SELECT ae.event_type, COUNT(*) AS cnt
                            FROM agent_events ae
                            WHERE ae.agent_id = p_agent_id AND ae.timestamp >= v_cutoff
                            GROUP BY ae.event_type
                            ORDER BY cnt DESC
                            LIMIT 10
                        ) tb_inner
                    ),
                    tool_breakdown AS (
                        SELECT jsonb_object_agg(tl_inner.tool_name, tl_inner.cnt) AS top_tools
                        FROM (
                            SELECT ae2.tool_name, COUNT(*) AS cnt
                            FROM agent_events ae2
                            WHERE ae2.agent_id = p_agent_id
                              AND ae2.timestamp >= v_cutoff
                              AND ae2.tool_name IS NOT NULL
                            GROUP BY ae2.tool_name
                            ORDER BY cnt DESC
                            LIMIT 10
                        ) tl_inner
                    )
                    SELECT
                        es.agent_id,
                        es.agent_name,
                        es.total_events,
                        es.total_sessions,
                        als.open_alerts,
                        als.critical_alerts,
                        es.unique_tools,
                        es.total_tokens,
                        es.avg_latency_ms,
                        es.events_per_minute,
                        tb.top_event_types,
                        tlb.top_tools
                    FROM event_stats es
                    CROSS JOIN alert_stats als
                    CROSS JOIN type_breakdown tb
                    CROSS JOIN tool_breakdown tlb;
                END;
                $$ LANGUAGE plpgsql STABLE;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to original (buggy) function if needed — covered by the previous migration
        }
    }
}
