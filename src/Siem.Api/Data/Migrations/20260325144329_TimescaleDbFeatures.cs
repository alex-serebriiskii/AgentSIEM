using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siem.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimescaleDbFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ================================================================
            // 1. EXTENSIONS
            // ================================================================

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // ================================================================
            // 2. HYPERTABLE
            // ================================================================

            migrationBuilder.Sql("""
                SELECT create_hypertable(
                    'agent_events',
                    'timestamp',
                    chunk_time_interval => INTERVAL '1 day',
                    if_not_exists => TRUE
                );
                """);

            // ================================================================
            // 3. INDEXES — agent_events
            // ================================================================

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_agent_time
                    ON agent_events (agent_id, timestamp DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_session_time
                    ON agent_events (session_id, timestamp ASC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_trace
                    ON agent_events (trace_id, timestamp ASC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_type_time
                    ON agent_events (event_type, timestamp DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_tool_time
                    ON agent_events (tool_name, timestamp DESC)
                    WHERE tool_name IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_properties
                    ON agent_events USING GIN (properties);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_content_hash
                    ON agent_events (content_hash, timestamp DESC)
                    WHERE content_hash IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_events_kafka
                    ON agent_events (kafka_partition, kafka_offset)
                    WHERE kafka_partition IS NOT NULL;
                """);

            // ================================================================
            // 4. INDEXES — alerts, sessions, rules
            // ================================================================

            migrationBuilder.Sql("""
                CREATE INDEX idx_alerts_status_time ON alerts (status, triggered_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_alerts_severity ON alerts (severity, triggered_at DESC)
                    WHERE status = 'open';
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_alerts_agent ON alerts (agent_id, triggered_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_alerts_rule ON alerts (rule_id, triggered_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_alerts_session ON alerts (session_id)
                    WHERE session_id IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_alert_events_event ON alert_events (event_id);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_sessions_agent ON agent_sessions (agent_id, started_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_sessions_alerts ON agent_sessions (has_alerts, last_event_at DESC)
                    WHERE has_alerts = TRUE;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_rules_enabled ON rules (enabled) WHERE enabled = TRUE;
                """);

            // ================================================================
            // 5. CHECK CONSTRAINTS & DEFAULTS
            // ================================================================

            migrationBuilder.Sql("""
                ALTER TABLE alerts
                    ADD CONSTRAINT chk_alerts_severity
                        CHECK (severity IN ('low','medium','high','critical')),
                    ADD CONSTRAINT chk_alerts_status
                        CHECK (status IN ('open','acknowledged','investigating','resolved','false_positive')),
                    ALTER COLUMN alert_id SET DEFAULT gen_random_uuid(),
                    ALTER COLUMN status SET DEFAULT 'open',
                    ALTER COLUMN context SET DEFAULT '{}'::jsonb,
                    ALTER COLUMN triggered_at SET DEFAULT NOW(),
                    ALTER COLUMN suppressed SET DEFAULT FALSE,
                    ALTER COLUMN labels SET DEFAULT '{}'::jsonb;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE rules
                    ADD CONSTRAINT chk_rules_severity
                        CHECK (severity IN ('low','medium','high','critical')),
                    ADD CONSTRAINT chk_rules_evaluation_type
                        CHECK (evaluation_type IN ('SingleEvent','Temporal','Sequence')),
                    ALTER COLUMN id SET DEFAULT gen_random_uuid(),
                    ALTER COLUMN enabled SET DEFAULT TRUE,
                    ALTER COLUMN severity SET DEFAULT 'medium',
                    ALTER COLUMN evaluation_type SET DEFAULT 'SingleEvent',
                    ALTER COLUMN actions_json SET DEFAULT '[]'::jsonb,
                    ALTER COLUMN tags SET DEFAULT '{}',
                    ALTER COLUMN created_at SET DEFAULT NOW(),
                    ALTER COLUMN updated_at SET DEFAULT NOW();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE agent_events
                    ALTER COLUMN event_id SET DEFAULT gen_random_uuid(),
                    ALTER COLUMN properties SET DEFAULT '{}'::jsonb,
                    ALTER COLUMN ingested_at SET DEFAULT NOW();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE agent_sessions
                    ALTER COLUMN event_count SET DEFAULT 1,
                    ALTER COLUMN has_alerts SET DEFAULT FALSE,
                    ALTER COLUMN alert_count SET DEFAULT 0,
                    ALTER COLUMN metadata SET DEFAULT '{}'::jsonb;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE managed_lists
                    ALTER COLUMN id SET DEFAULT gen_random_uuid(),
                    ALTER COLUMN enabled SET DEFAULT TRUE,
                    ALTER COLUMN created_at SET DEFAULT NOW(),
                    ALTER COLUMN updated_at SET DEFAULT NOW();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE managed_list_members
                    ALTER COLUMN added_at SET DEFAULT NOW();
                """);

            // ================================================================
            // 6. CONTINUOUS AGGREGATES
            // ================================================================

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW agent_activity_hourly
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('1 hour', timestamp)    AS bucket,
                    agent_id,
                    agent_name,
                    event_type,
                    COUNT(*)                            AS event_count,
                    SUM(input_tokens)                   AS total_input_tokens,
                    SUM(output_tokens)                  AS total_output_tokens,
                    SUM(input_tokens + output_tokens)
                        FILTER (WHERE input_tokens IS NOT NULL)
                                                        AS total_tokens,
                    AVG(latency_ms)                     AS avg_latency_ms,
                    MAX(latency_ms)                     AS max_latency_ms,
                    percentile_cont(0.95)
                        WITHIN GROUP (ORDER BY latency_ms)
                                                        AS p95_latency_ms,
                    COUNT(DISTINCT session_id)          AS unique_sessions,
                    COUNT(DISTINCT tool_name)
                        FILTER (WHERE tool_name IS NOT NULL)
                                                        AS unique_tools_used
                FROM agent_events
                GROUP BY bucket, agent_id, agent_name, event_type
                WITH NO DATA;
                """);

            migrationBuilder.Sql("""
                SELECT add_continuous_aggregate_policy('agent_activity_hourly',
                    start_offset    => INTERVAL '3 hours',
                    end_offset      => INTERVAL '5 minutes',
                    schedule_interval => INTERVAL '5 minutes',
                    if_not_exists   => TRUE
                );
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW agent_activity_daily
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('1 day', timestamp)     AS bucket,
                    agent_id,
                    agent_name,
                    COUNT(*)                            AS event_count,
                    SUM(input_tokens + output_tokens)
                        FILTER (WHERE input_tokens IS NOT NULL)
                                                        AS total_tokens,
                    COUNT(DISTINCT session_id)          AS unique_sessions,
                    COUNT(DISTINCT event_type)          AS event_type_variety,
                    MIN(timestamp)                      AS first_seen,
                    MAX(timestamp)                      AS last_seen
                FROM agent_events
                GROUP BY bucket, agent_id, agent_name
                WITH NO DATA;
                """);

            migrationBuilder.Sql("""
                SELECT add_continuous_aggregate_policy('agent_activity_daily',
                    start_offset    => INTERVAL '3 days',
                    end_offset      => INTERVAL '1 hour',
                    schedule_interval => INTERVAL '1 hour',
                    if_not_exists   => TRUE
                );
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW tool_usage_hourly
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('1 hour', timestamp)    AS bucket,
                    tool_name,
                    agent_id,
                    COUNT(*)                            AS invocation_count,
                    AVG(latency_ms)                     AS avg_latency_ms,
                    COUNT(DISTINCT session_id)          AS unique_sessions
                FROM agent_events
                WHERE event_type = 'tool_invocation' AND tool_name IS NOT NULL
                GROUP BY bucket, tool_name, agent_id
                WITH NO DATA;
                """);

            migrationBuilder.Sql("""
                SELECT add_continuous_aggregate_policy('tool_usage_hourly',
                    start_offset    => INTERVAL '3 hours',
                    end_offset      => INTERVAL '5 minutes',
                    schedule_interval => INTERVAL '5 minutes',
                    if_not_exists   => TRUE
                );
                """);

            // ================================================================
            // 7. COMPRESSION & RETENTION POLICIES
            // ================================================================

            migrationBuilder.Sql("""
                ALTER TABLE agent_events SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'agent_id, event_type',
                    timescaledb.compress_orderby = 'timestamp DESC'
                );
                """);

            migrationBuilder.Sql("""
                SELECT add_compression_policy('agent_events',
                    compress_after => INTERVAL '7 days',
                    if_not_exists  => TRUE
                );
                """);

            migrationBuilder.Sql("""
                SELECT add_retention_policy('agent_events',
                    drop_after    => INTERVAL '90 days',
                    if_not_exists => TRUE
                );
                """);

            migrationBuilder.Sql("""
                SELECT add_retention_policy('agent_activity_hourly',
                    drop_after    => INTERVAL '1 year',
                    if_not_exists => TRUE
                );
                """);

            migrationBuilder.Sql("""
                SELECT add_retention_policy('agent_activity_daily',
                    drop_after    => INTERVAL '3 years',
                    if_not_exists => TRUE
                );
                """);

            migrationBuilder.Sql("""
                SELECT add_retention_policy('tool_usage_hourly',
                    drop_after    => INTERVAL '1 year',
                    if_not_exists => TRUE
                );
                """);

            // ================================================================
            // 8. HELPER FUNCTIONS
            // ================================================================

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION get_session_timeline(
                    p_session_id TEXT,
                    p_limit INTEGER DEFAULT 1000
                )
                RETURNS TABLE (
                    event_id        UUID,
                    "timestamp"     TIMESTAMPTZ,
                    event_type      TEXT,
                    agent_id        TEXT,
                    tool_name       TEXT,
                    model_id        TEXT,
                    input_tokens    INTEGER,
                    output_tokens   INTEGER,
                    latency_ms      DOUBLE PRECISION,
                    properties      JSONB,
                    alert_ids       UUID[],
                    alert_severities TEXT[]
                ) AS $$
                BEGIN
                    RETURN QUERY
                    SELECT
                        e.event_id,
                        e.timestamp,
                        e.event_type,
                        e.agent_id,
                        e.tool_name,
                        e.model_id,
                        e.input_tokens,
                        e.output_tokens,
                        e.latency_ms,
                        e.properties,
                        ARRAY_AGG(DISTINCT ae.alert_id) FILTER (WHERE ae.alert_id IS NOT NULL),
                        ARRAY_AGG(DISTINCT a.severity)  FILTER (WHERE a.severity IS NOT NULL)
                    FROM agent_events e
                    LEFT JOIN alert_events ae ON ae.event_id = e.event_id
                    LEFT JOIN alerts a ON a.alert_id = ae.alert_id
                    WHERE e.session_id = p_session_id
                    GROUP BY e.event_id, e.timestamp, e.event_type, e.agent_id,
                             e.tool_name, e.model_id, e.input_tokens, e.output_tokens,
                             e.latency_ms, e.properties
                    ORDER BY e.timestamp ASC
                    LIMIT p_limit;
                END;
                $$ LANGUAGE plpgsql STABLE;
                """);

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
                            e.agent_id,
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
                        SELECT jsonb_object_agg(event_type, cnt) AS top_event_types
                        FROM (
                            SELECT event_type, COUNT(*) AS cnt
                            FROM agent_events
                            WHERE agent_id = p_agent_id AND timestamp >= v_cutoff
                            GROUP BY event_type
                            ORDER BY cnt DESC
                            LIMIT 10
                        ) t
                    ),
                    tool_breakdown AS (
                        SELECT jsonb_object_agg(tool_name, cnt) AS top_tools
                        FROM (
                            SELECT tool_name, COUNT(*) AS cnt
                            FROM agent_events
                            WHERE agent_id = p_agent_id
                              AND timestamp >= v_cutoff
                              AND tool_name IS NOT NULL
                            GROUP BY tool_name
                            ORDER BY cnt DESC
                            LIMIT 10
                        ) t
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

            // ================================================================
            // 9. SESSION MAINTENANCE FUNCTIONS
            // ================================================================

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION upsert_session(
                    p_session_id TEXT,
                    p_agent_id TEXT,
                    p_agent_name TEXT,
                    p_timestamp TIMESTAMPTZ
                )
                RETURNS VOID AS $$
                BEGIN
                    INSERT INTO agent_sessions (session_id, agent_id, agent_name, started_at, last_event_at)
                    VALUES (p_session_id, p_agent_id, p_agent_name, p_timestamp, p_timestamp)
                    ON CONFLICT (session_id) DO UPDATE SET
                        last_event_at = GREATEST(agent_sessions.last_event_at, EXCLUDED.last_event_at),
                        event_count = agent_sessions.event_count + 1;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION update_session_alerts(
                    p_session_id TEXT,
                    p_severity TEXT
                )
                RETURNS VOID AS $$
                BEGIN
                    UPDATE agent_sessions SET
                        has_alerts = TRUE,
                        alert_count = alert_count + 1,
                        max_severity = CASE
                            WHEN max_severity IS NULL THEN p_severity
                            WHEN p_severity = 'critical' THEN 'critical'
                            WHEN p_severity = 'high' AND max_severity NOT IN ('critical') THEN 'high'
                            WHEN p_severity = 'medium' AND max_severity NOT IN ('critical','high') THEN 'medium'
                            ELSE max_severity
                        END
                    WHERE session_id = p_session_id;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Functions
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS update_session_alerts(TEXT, TEXT);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS upsert_session(TEXT, TEXT, TEXT, TIMESTAMPTZ);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_agent_risk_summary(TEXT, INTERVAL);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_session_timeline(TEXT, INTEGER);");

            // Retention policies (must drop before aggregates)
            migrationBuilder.Sql("SELECT remove_retention_policy('tool_usage_hourly', if_exists => TRUE);");
            migrationBuilder.Sql("SELECT remove_retention_policy('agent_activity_daily', if_exists => TRUE);");
            migrationBuilder.Sql("SELECT remove_retention_policy('agent_activity_hourly', if_exists => TRUE);");
            migrationBuilder.Sql("SELECT remove_retention_policy('agent_events', if_exists => TRUE);");

            // Compression policy
            migrationBuilder.Sql("SELECT remove_compression_policy('agent_events', if_exists => TRUE);");

            // Continuous aggregates
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS tool_usage_hourly CASCADE;");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS agent_activity_daily CASCADE;");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS agent_activity_hourly CASCADE;");

            // Indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_rules_enabled;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_sessions_alerts;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_sessions_agent;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alert_events_event;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alerts_session;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alerts_rule;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alerts_agent;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alerts_severity;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_alerts_status_time;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_kafka;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_content_hash;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_properties;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_tool_time;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_type_time;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_trace;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_session_time;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_events_agent_time;");

            // Check constraints
            migrationBuilder.Sql("ALTER TABLE rules DROP CONSTRAINT IF EXISTS chk_rules_evaluation_type;");
            migrationBuilder.Sql("ALTER TABLE rules DROP CONSTRAINT IF EXISTS chk_rules_severity;");
            migrationBuilder.Sql("ALTER TABLE alerts DROP CONSTRAINT IF EXISTS chk_alerts_status;");
            migrationBuilder.Sql("ALTER TABLE alerts DROP CONSTRAINT IF EXISTS chk_alerts_severity;");

            // Extensions (be careful — other schemas may depend on these)
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS timescaledb;");
        }
    }
}
