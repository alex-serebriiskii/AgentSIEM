using Microsoft.EntityFrameworkCore;
using Prometheus;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Hubs;
using Siem.Api.Kafka;
using Siem.Api.Services;
using Siem.Rules.Core;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------------------------------------------------------------------------
// Data layer
// ---------------------------------------------------------------------------
builder.Services.AddNpgsqlDataSource(
    configuration.GetConnectionString("TimescaleDb")
        ?? throw new InvalidOperationException("Missing connection string 'TimescaleDb'"));

builder.Services.AddDbContext<SiemDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("TimescaleDb")));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Missing connection string 'Redis'")));

// ---------------------------------------------------------------------------
// Core services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ListCacheService>();
builder.Services.AddScoped<RuleLoadingService>();
builder.Services.AddSingleton<RedisStateProvider>();
builder.Services.AddSingleton<Evaluator.IStateProvider>(sp =>
    sp.GetRequiredService<RedisStateProvider>());
builder.Services.AddSingleton<CompiledRulesCache>();
builder.Services.AddSingleton<RecompilationCoordinator>();
builder.Services.AddSingleton<IRecompilationCoordinator>(sp =>
    sp.GetRequiredService<RecompilationCoordinator>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RecompilationCoordinator>());

// ---------------------------------------------------------------------------
// Kafka consumer pipeline
// ---------------------------------------------------------------------------
builder.Services.AddKafkaPipeline(configuration);

// ---------------------------------------------------------------------------
// Alert pipeline & notifications
// ---------------------------------------------------------------------------
builder.Services.AddAlertPipeline(configuration);

// ---------------------------------------------------------------------------
// Background services
// ---------------------------------------------------------------------------
builder.Services.Configure<ToolAnomalyConfig>(
    configuration.GetSection("ToolAnomalyDetector"));
builder.Services.AddHostedService<ToolAnomalyDetector>();

// ---------------------------------------------------------------------------
// Metrics / Prometheus
// ---------------------------------------------------------------------------
// prometheus-net bridges System.Diagnostics.Metrics to Prometheus format.
// All Meter instances across the codebase (Siem.Pipeline, Siem.Alerts,
// Siem.Kafka, Siem.Storage, Siem.Notifications, Siem.Anomaly) are automatically
// exported at /metrics via the MeterAdapter.
Metrics.SuppressDefaultMetrics(new SuppressDefaultMetricOptions
{
    SuppressProcessMetrics = false
});

// ---------------------------------------------------------------------------
// Web / API infrastructure
// ---------------------------------------------------------------------------
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

// ---------------------------------------------------------------------------
// Build & configure middleware
// ---------------------------------------------------------------------------
var app = builder.Build();

// Run EF Core migrations on startup (safe for development; for production
// consider running migrations as a separate step before deployment).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SiemDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpMetrics(); // Track HTTP request duration/count per endpoint
app.MapControllers();
app.MapHub<AlertHub>("/hubs/alerts");
app.MapHealthChecks("/health");
app.MapMetrics(); // Prometheus scraping endpoint at /metrics

app.Run();
