using Microsoft.EntityFrameworkCore;
using Siem.Api.Data.Entities;

namespace Siem.Api.Data;

public class SiemDbContext : DbContext
{
    public SiemDbContext(DbContextOptions<SiemDbContext> options) : base(options) { }

    public DbSet<RuleEntity> Rules => Set<RuleEntity>();
    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();
    public DbSet<AlertEventEntity> AlertEvents => Set<AlertEventEntity>();
    public DbSet<AgentSessionEntity> AgentSessions => Set<AgentSessionEntity>();
    public DbSet<ManagedListEntity> ManagedLists => Set<ManagedListEntity>();
    public DbSet<ListMemberEntity> ListMembers => Set<ListMemberEntity>();
    public DbSet<SuppressionEntity> Suppressions => Set<SuppressionEntity>();
    public DbSet<AgentEventReadModel> AgentEvents => Set<AgentEventReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Rules
        modelBuilder.Entity<RuleEntity>(e =>
        {
            e.ToTable("rules");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.Name).HasColumnName("name");
            e.Property(r => r.Description).HasColumnName("description");
            e.Property(r => r.Enabled).HasColumnName("enabled");
            e.Property(r => r.Severity).HasColumnName("severity");
            e.Property(r => r.ConditionJson).HasColumnName("condition_json").HasColumnType("jsonb");
            e.Property(r => r.EvaluationType).HasColumnName("evaluation_type");
            e.Property(r => r.TemporalConfig).HasColumnName("temporal_config").HasColumnType("jsonb");
            e.Property(r => r.SequenceConfig).HasColumnName("sequence_config").HasColumnType("jsonb");
            e.Property(r => r.ActionsJson).HasColumnName("actions_json").HasColumnType("jsonb");
            e.Property(r => r.Tags).HasColumnName("tags");
            e.Property(r => r.CreatedBy).HasColumnName("created_by");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        });

        // Alerts
        modelBuilder.Entity<AlertEntity>(e =>
        {
            e.ToTable("alerts");
            e.HasKey(a => a.AlertId);
            e.Property(a => a.AlertId).HasColumnName("alert_id");
            e.Property(a => a.RuleId).HasColumnName("rule_id");
            e.Property(a => a.RuleName).HasColumnName("rule_name");
            e.Property(a => a.Severity).HasColumnName("severity");
            e.Property(a => a.Status).HasColumnName("status");
            e.Property(a => a.Title).HasColumnName("title");
            e.Property(a => a.Detail).HasColumnName("detail");
            e.Property(a => a.Context).HasColumnName("context").HasColumnType("jsonb");
            e.Property(a => a.AgentId).HasColumnName("agent_id");
            e.Property(a => a.SessionId).HasColumnName("session_id");
            e.Property(a => a.TriggeredAt).HasColumnName("triggered_at");
            e.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at");
            e.Property(a => a.ResolvedAt).HasColumnName("resolved_at");
            e.Property(a => a.AssignedTo).HasColumnName("assigned_to");
            e.Property(a => a.ResolutionNote).HasColumnName("resolution_note");
            e.Property(a => a.Labels).HasColumnName("labels").HasColumnType("jsonb");
            e.Property(a => a.Suppressed).HasColumnName("suppressed");
            e.Property(a => a.SuppressedBy).HasColumnName("suppressed_by");
            e.Property(a => a.SuppressionExpiresAt).HasColumnName("suppression_expires_at");
            e.HasMany(a => a.AlertEvents).WithOne(ae => ae.Alert).HasForeignKey(ae => ae.AlertId);
        });

        // Alert Events (junction)
        modelBuilder.Entity<AlertEventEntity>(e =>
        {
            e.ToTable("alert_events");
            e.HasKey(ae => new { ae.AlertId, ae.EventId });
            e.Property(ae => ae.AlertId).HasColumnName("alert_id");
            e.Property(ae => ae.EventId).HasColumnName("event_id");
            e.Property(ae => ae.EventTimestamp).HasColumnName("event_timestamp");
            e.Property(ae => ae.SequenceOrder).HasColumnName("sequence_order");
        });

        // Agent Sessions
        modelBuilder.Entity<AgentSessionEntity>(e =>
        {
            e.ToTable("agent_sessions");
            e.HasKey(s => s.SessionId);
            e.Property(s => s.SessionId).HasColumnName("session_id");
            e.Property(s => s.AgentId).HasColumnName("agent_id");
            e.Property(s => s.AgentName).HasColumnName("agent_name");
            e.Property(s => s.StartedAt).HasColumnName("started_at");
            e.Property(s => s.LastEventAt).HasColumnName("last_event_at");
            e.Property(s => s.EventCount).HasColumnName("event_count");
            e.Property(s => s.HasAlerts).HasColumnName("has_alerts");
            e.Property(s => s.AlertCount).HasColumnName("alert_count");
            e.Property(s => s.MaxSeverity).HasColumnName("max_severity");
            e.Property(s => s.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        });

        // Managed Lists
        modelBuilder.Entity<ManagedListEntity>(e =>
        {
            e.ToTable("managed_lists");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasColumnName("id");
            e.Property(l => l.Name).HasColumnName("name");
            e.Property(l => l.Description).HasColumnName("description");
            e.Property(l => l.Enabled).HasColumnName("enabled");
            e.Property(l => l.CreatedAt).HasColumnName("created_at");
            e.Property(l => l.UpdatedAt).HasColumnName("updated_at");
            e.HasMany(l => l.Members).WithOne(m => m.List).HasForeignKey(m => m.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        // List Members
        modelBuilder.Entity<ListMemberEntity>(e =>
        {
            e.ToTable("managed_list_members");
            e.HasKey(m => new { m.ListId, m.Value });
            e.Property(m => m.ListId).HasColumnName("list_id");
            e.Property(m => m.Value).HasColumnName("value");
            e.Property(m => m.AddedAt).HasColumnName("added_at");
        });

        // Suppressions
        modelBuilder.Entity<SuppressionEntity>(e =>
        {
            e.ToTable("suppressions");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.RuleId).HasColumnName("rule_id");
            e.Property(s => s.AgentId).HasColumnName("agent_id");
            e.Property(s => s.Reason).HasColumnName("reason");
            e.Property(s => s.CreatedBy).HasColumnName("created_by");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");
            e.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        });

        // Agent Events (read-only view for enrichment queries)
        modelBuilder.Entity<AgentEventReadModel>(e =>
        {
            e.ToTable("agent_events");
            e.Property(ev => ev.EventId).HasColumnName("event_id");
            e.Property(ev => ev.Timestamp).HasColumnName("timestamp");
            e.Property(ev => ev.AgentId).HasColumnName("agent_id");
            e.Property(ev => ev.AgentName).HasColumnName("agent_name");
            e.Property(ev => ev.SessionId).HasColumnName("session_id");
            e.Property(ev => ev.TraceId).HasColumnName("trace_id");
            e.Property(ev => ev.EventType).HasColumnName("event_type");
            e.Property(ev => ev.SeverityHint).HasColumnName("severity_hint");
            e.Property(ev => ev.ModelId).HasColumnName("model_id");
            e.Property(ev => ev.InputTokens).HasColumnName("input_tokens");
            e.Property(ev => ev.OutputTokens).HasColumnName("output_tokens");
            e.Property(ev => ev.LatencyMs).HasColumnName("latency_ms");
            e.Property(ev => ev.ToolName).HasColumnName("tool_name");
            e.Property(ev => ev.ToolInput).HasColumnName("tool_input");
            e.Property(ev => ev.ToolOutput).HasColumnName("tool_output");
            e.Property(ev => ev.ContentHash).HasColumnName("content_hash");
            e.Property(ev => ev.Properties).HasColumnName("properties").HasColumnType("jsonb");
            e.Property(ev => ev.IngestedAt).HasColumnName("ingested_at");
            e.Property(ev => ev.KafkaPartition).HasColumnName("kafka_partition");
            e.Property(ev => ev.KafkaOffset).HasColumnName("kafka_offset");
            e.Property(ev => ev.SourceSdk).HasColumnName("source_sdk");
        });
    }
}
