using CashFlow.Api.Infrastructure;
using CashFlow.Api.Infrastructure.Repositories;
using CashFlow.Api.Application;
using CashFlow.Api.Workers;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OpenTelemetry.Metrics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);


// LOGS
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/log.txt")
    .CreateLogger();

builder.Host.UseSerilog();

// DI
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddScoped<LancamentoRepository>();
builder.Services.AddScoped<ConsolidadoRepository>();
builder.Services.AddScoped<LancamentoService>();
builder.Services.AddScoped<IConsolidadoRepository,ConsolidadoRepository>();
builder.Services.AddScoped<IOutboxRepository,OutboxRepository>();
builder.Services.AddScoped<IEventosProcessadosRepository,EventosProcessadosRepository>();
builder.Services.AddSingleton<KafkaProducer>();
builder.Services.AddHostedService<KafkaConsumer>();

builder.Services.AddHostedService<OutboxWorker>();

builder.Services.AddHostedService<ConsolidadoWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Health
builder.Services.AddHealthChecks();


// Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });


 builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();
    });


var app = builder.Build();


app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health");

// Prometheus endpoint
app.MapPrometheusScrapingEndpoint("/metrics");

// Important for Docker
app.Urls.Add("http://0.0.0.0:8080");

app.Run();
