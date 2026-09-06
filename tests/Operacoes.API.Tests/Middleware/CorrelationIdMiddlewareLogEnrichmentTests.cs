using Microsoft.AspNetCore.Http;
using Operacoes.API.Middleware;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Operacoes.API.Tests.Middleware;

public sealed class CorrelationIdMiddlewareLogEnrichmentTests
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPushCorrelationIdToAmbientLogContext_SoStructuredLogsCarryIt()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var expectedCorrelationId = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdHeader] = expectedCorrelationId;
        context.Response.Body = new MemoryStream();

        RequestDelegate next = _ =>
        {
            logger.Information("Handling request inside pipeline");
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next);

        await middleware.InvokeAsync(context);

        var loggedEvent = Assert.Single(sink.Events);
        Assert.True(loggedEvent.Properties.TryGetValue("CorrelationId", out var property));
        var scalarValue = Assert.IsType<ScalarValue>(property);
        Assert.Equal(expectedCorrelationId, scalarValue.Value);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotLeakCorrelationIdToLogsOutsideThePipelineScope()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new CorrelationIdMiddleware(next);

        await middleware.InvokeAsync(context);

        logger.Information("Logging after the middleware scope has ended");

        var loggedEvent = Assert.Single(sink.Events);
        Assert.False(loggedEvent.Properties.ContainsKey("CorrelationId"));
    }
}
