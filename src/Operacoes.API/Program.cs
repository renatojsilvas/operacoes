using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using Operacoes.API;
using Operacoes.API.Endpoints;
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
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
var app = builder.Build();
NormalizeApiKeyConfiguration(app.Configuration);
NormalizeHubApiKeyConfiguration(app.Configuration);
ConnectionStringGuard.Validate(app.Configuration, app.Environment);
ApiKeyGuard.Validate(app.Configuration, app.Environment);
HubConfigGuard.Validate(app.Configuration, app.Environment);
await app.InitializeDatabaseAsync();
app.UseForwardedHeaders();
var httpMetricsExcludedPaths = app.Configuration.GetSection("Metrics:ExcludedPaths").Get<string[]>() ?? [];
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = exception is BadHttpRequestException badHttpRequestException
            ? badHttpRequestException.StatusCode
            : StatusCodes.Status500InternalServerError;
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new Microsoft.AspNetCore.Http.ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = context.Response.StatusCode },
        });
    });
});
app.UseSerilogDefaults();
app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapOperacoesEndpoints();
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
static void NormalizeHubApiKeyConfiguration(IConfiguration configuration)
{
    var rawKey = configuration["Hub:ApiKey"];
    if (rawKey is not null)
    {
        configuration["Hub:ApiKey"] = rawKey.Trim();
    }
}
public partial class Program;
