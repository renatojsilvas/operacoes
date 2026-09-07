using Operacoes.Application.Catalogo;
using Operacoes.Application.Common.Interfaces;
using Operacoes.Application.Operacoes;
using Operacoes.Application.Outbox;
using Operacoes.Infrastructure.Caching;
using Operacoes.Infrastructure.Catalogo;
using Operacoes.Infrastructure.Http;
using Operacoes.Infrastructure.Messaging;
using Operacoes.Infrastructure.Observability;
using Operacoes.Infrastructure.Outbox;
using Operacoes.Infrastructure.Persistence;
using Operacoes.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using Polly;
using Quartz;

namespace Operacoes.Infrastructure;

public static class DependencyInjection
{
    private const int NpgsqlMaxPoolSize = 5;
    private const long MemoryCacheSizeLimit = 1_000;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection")!)
        {
            NoResetOnClose = true,
            MaxPoolSize = NpgsqlMaxPoolSize
        }.ConnectionString;
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IApiKeyMetrics, ApiKeyMetrics>();
        services.AddSingleton<IBusinessMetrics, BusinessMetrics>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOperacaoReadRepository, OperacaoReadRepository>();
        services.AddScoped<IOperacaoWriteRepository, OperacaoWriteRepository>();
        services.AddScoped<IOutboxWriteRepository, OutboxWriteRepository>();
        services.AddScoped<IOutboxReadRepository, OutboxReadRepository>();
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
        services.AddSingleton<RelayOutboxFalhaLogThrottle>();

        services.AddMemoryCache(options => options.SizeLimit = MemoryCacheSizeLimit);

        services.AddScoped<ContentVersionProvider>();
        services.AddScoped<IContentVersionProvider>(sp =>
            new CachedContentVersionProvider(
                sp.GetRequiredService<ContentVersionProvider>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddHttpClient<HubCatalogoClient>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var baseUrl = config["Hub:BaseUrl"];
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
            {
                client.BaseAddress = baseUri;
            }
            var apiKey = config["Hub:ApiKey"]?.Trim();
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }
        })
        .AddHubResilienceHandler(configuration);

        services.AddScoped<IHubCatalogoClient>(sp =>
            new CachedHubCatalogoClient(
                sp.GetRequiredService<HubCatalogoClient>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConfiguration>()));

        var relayAgendamentoAtivo = configuration.GetValue<bool?>("Outbox:Relay:AgendamentoAtivo") ?? true;
        var relayIntervaloSegundos = configuration.GetValue<int?>("Outbox:Relay:IntervaloSegundos") ?? 5;

        services.AddQuartz(q =>
        {
            if (relayAgendamentoAtivo)
            {
                var relayJobKey = new JobKey("relay-outbox");
                q.AddJob<RelayOutboxJob>(opts => opts.WithIdentity(relayJobKey));
                q.AddTrigger(opts => opts
                    .ForJob(relayJobKey)
                    .WithIdentity("relay-outbox-trigger")
                    .WithSimpleSchedule(x => x
                        .WithIntervalInSeconds(relayIntervaloSegundos)
                        .RepeatForever()
                        .WithMisfireHandlingInstructionNextWithRemainingCount()));
            }
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }

    public static IHttpResiliencePipelineBuilder AddHubResilienceHandler(
        this IHttpClientBuilder builder, IConfiguration configuration)
    {
        return builder.AddResilienceHandler("hub-catalogo-resilience", pipeline =>
        {
            var section = configuration.GetSection("Resilience:Hub");
            var totalTimeout = section.GetValue<TimeSpan?>("TotalTimeout") ?? TimeSpan.FromSeconds(3);
            var retryMaxAttempts = section.GetValue<int?>("Retry:MaxAttempts") ?? 2;
            var retryBaseDelay = section.GetValue<TimeSpan?>("Retry:BaseDelay") ?? TimeSpan.FromMilliseconds(200);
            var failureRatio = section.GetValue<double?>("CircuitBreaker:FailureRatio") ?? 0.5;
            var minimumThroughput = section.GetValue<int?>("CircuitBreaker:MinimumThroughput") ?? 10;
            var samplingDuration = section.GetValue<TimeSpan?>("CircuitBreaker:SamplingDuration") ?? TimeSpan.FromSeconds(30);
            var breakDuration = section.GetValue<TimeSpan?>("CircuitBreaker:BreakDuration") ?? TimeSpan.FromSeconds(15);
            var attemptTimeout = section.GetValue<TimeSpan?>("AttemptTimeout") ?? TimeSpan.FromSeconds(1);
            pipeline
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = totalTimeout })
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = retryBaseDelay,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = failureRatio,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = samplingDuration,
                    BreakDuration = breakDuration,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = attemptTimeout });
        });
    }
}
