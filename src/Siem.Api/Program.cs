using Microsoft.EntityFrameworkCore;
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<AlertHub>("/hubs/alerts");
app.MapHealthChecks("/health");

app.Run();
