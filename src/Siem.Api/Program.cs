using Microsoft.EntityFrameworkCore;
using Prometheus;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Hubs;
using Siem.Api.Kafka;
using Siem.Api.Services;
using FluentValidation;
using Siem.Api.Validators;
using Siem.Rules.Core;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------------------------------------------------------------------------
// Data layer
// ---------------------------------------------------------------------------
builder.Services.AddNpgsqlDataSource(
    configuration.GetConnectionString("TimescaleDb")
        ?? throw new InvalidOperationException("Missing connection string 'TimescaleDb'"));

builder.Services.AddDbContextFactory<SiemDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("TimescaleDb")));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Missing connection string 'Redis'")));

// ---------------------------------------------------------------------------
// Core services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IListCacheService, ListCacheService>();
builder.Services.AddSingleton<RuleLoadingService>();
builder.Services.AddSingleton<Evaluator.IStateProvider, RedisStateProvider>();
builder.Services.AddSingleton<ICompiledRulesCache, CompiledRulesCache>();
builder.Services.AddOptions<RecompilationConfig>()
    .Bind(configuration.GetSection("Recompilation"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RecompilationConfig>>().Value);
builder.Services.AddSingleton<ICompilationNotifier, CompilationNotifier>();
builder.Services.AddSingleton<IRuleCompilationOrchestrator, RuleCompilationOrchestrator>();
builder.Services.AddSingleton<RecompilationCoordinator>();
builder.Services.AddSingleton<IRecompilationCoordinator>(sp =>
    sp.GetRequiredService<RecompilationCoordinator>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RecompilationCoordinator>());

builder.Services.AddOptions<PaginationConfig>()
    .Bind(configuration.GetSection("Pagination"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<PaginationConfig>>().Value);

// ---------------------------------------------------------------------------
// API services (controller backing)
// ---------------------------------------------------------------------------
builder.Services.AddScoped<ISuppressionService, SuppressionService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IRuleService, RuleService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IListService, ListService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

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
builder.Services.AddValidatorsFromAssemblyContaining<CreateRuleRequestValidator>();
builder.Services.AddSignalR();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<FluentValidationFilter>();
});
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
