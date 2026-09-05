using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using Operacoes.API;
using Operacoes.API.Extensions;
using Operacoes.API.Middleware;
using Operacoes.Application;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure;
using IResult = Microsoft.AspNetCore.Http.IResult;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

NormalizeApiKeyConfiguration(app.Configuration);

ConnectionStringGuard.Validate(app.Configuration, app.Environment);
ApiKeyGuard.Validate(app.Configuration, app.Environment);

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();

var httpMetricsExcludedPaths = app.Configuration.GetSection("Metrics:ExcludedPaths").Get<string[]>() ?? [];
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());

app.UseExceptionHandler();

app.UseSerilogDefaults();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Nenhum endpoint de negocio neste F1 (ver docs/ROADMAP.md). O grupo /v1 nasce no F3,
// quando POST /operacoes existir.

app.MapMetrics();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/_test/throw", IResult () => throw new InvalidOperationException("Forced exception for exception handler testing."))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/validation", IResult () =>
            Result.Failure(new Error("Test.Validation", "Validation failure for testing.", ErrorType.Validation))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/not-found", IResult () =>
            Result.Failure(new Error("Test.NotFound", "Not found for testing.", ErrorType.NotFound))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/conflict", IResult () =>
            Result.Failure(new Error("Test.Conflict", "Conflict for testing.", ErrorType.Conflict))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/success", IResult () =>
            Result.Success().ToHttpResult(() => Results.Ok(new { ok = true })))
        .ExcludeFromDescription();
}

app.Run();

static void NormalizeApiKeyConfiguration(IConfiguration configuration)
{
    var rawKey = configuration["ApiKey:Key"];
    if (rawKey is not null)
    {
        configuration["ApiKey:Key"] = rawKey.Trim();
    }
}

public partial class Program;
