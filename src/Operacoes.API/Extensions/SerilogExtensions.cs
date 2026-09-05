using Serilog;
using Serilog.Sinks.Grafana.Loki;
using Operacoes.API.Middleware;

namespace Operacoes.API.Extensions;

public static class SerilogExtensions
{
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        var lokiUri = builder.Configuration["Loki:Uri"] ?? "http://localhost:3100";

        builder.Host.UseSerilog((context, configuration) =>
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.WithProperty("service", "operacoes")
                .Enrich.WithProperty("environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
                .WriteTo.GrafanaLoki(
                    lokiUri,
                    labels: [new LokiLabel { Key = "job", Value = "operacoes" }],
                    textFormatter: new Serilog.Formatting.Compact.CompactJsonFormatter()));

        return builder;
    }

    public static WebApplication UseSerilogDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();

        return app;
    }
}
