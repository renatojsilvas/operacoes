using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Operacoes.Application.Catalogo;
using Operacoes.Infrastructure.Outbox;
using Operacoes.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Operacoes.API.Tests.Integration;

public sealed class RelayReadinessTests : IAsyncLifetime
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ValidApiKey = "integration-test-api-key-0123456789";

    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string ApiKeyEnvVar = "ApiKey__Key";
    private const string RabbitMqHostEnvVar = "RabbitMq__Host";
    private const string RabbitMqPortEnvVar = "RabbitMq__Port";
    private const string RelayAgendamentoAtivoEnvVar = "Outbox__Relay__AgendamentoAtivo";
    private const string RelayIntervaloSegundosEnvVar = "Outbox__Relay__IntervaloSegundos";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly FakeHubCatalogoClient _hubCatalogoClient = new();
    private readonly FakeLogger<RelayOutboxJob> _relayJobLogger = new();
    private BrokerIndisponivelFactory _factory = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();

            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, ValidApiKey);
            Environment.SetEnvironmentVariable(RabbitMqHostEnvVar, "127.0.0.1");
            Environment.SetEnvironmentVariable(RabbitMqPortEnvVar, "1");
            Environment.SetEnvironmentVariable(RelayAgendamentoAtivoEnvVar, "true");
            Environment.SetEnvironmentVariable(RelayIntervaloSegundosEnvVar, "1");

            _factory = new BrokerIndisponivelFactory(_hubCatalogoClient, _relayJobLogger);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        catch
        {
            ClearEnvironmentVariables();
            try
            {
                await _postgres.DisposeAsync();
            }
            catch
            {
            }

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _factory.DisposeAsync();
            await _postgres.DisposeAsync();
        }
        finally
        {
            ClearEnvironmentVariables();
        }
    }

    private static void ClearEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, null);
        Environment.SetEnvironmentVariable(RabbitMqHostEnvVar, null);
        Environment.SetEnvironmentVariable(RabbitMqPortEnvVar, null);
        Environment.SetEnvironmentVariable(RelayAgendamentoAtivoEnvVar, null);
        Environment.SetEnvironmentVariable(RelayIntervaloSegundosEnvVar, null);
    }

    [Fact]
    public async Task RelayLigadoComBrokerInacessivel_ReadinessContinuaHealthyEEscritaContinuaGravando_EORelayRealmenteFalhou()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, ValidApiKey);

        var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = JsonContent.Create(new
            {
                clienteId = "cliente-readiness",
                instrumentoId = "td:tesouro-selic-2029",
                tipo = "aporte",
                quantidade = 10m,
                valorFinanceiro = 1000m,
                dataEvento = "2020-01-01",
                estornaOperacaoId = (string?)null,
                valorOrigemSaldo = 500m,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "chave-readiness-1");

        var postResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var readyResponseAposEscrita = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponseAposEscrita.StatusCode);

        var falhaRegistrada = await AguardarAsync(
            () => _relayJobLogger.Entries.Any(e => e.Level == LogLevel.Error),
            TimeSpan.FromSeconds(15));

        Assert.True(
            falhaRegistrada,
            "o relay tem que ter tentado publicar e falhado (log de erro do RelayOutboxJob) enquanto o " +
            "broker estava inacessível; sem isto, o teste passaria mesmo se o relay nunca tivesse rodado.");
    }

    private static async Task<bool> AguardarAsync(Func<bool> condicao, TimeSpan timeout)
    {
        var limite = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < limite)
        {
            if (condicao())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return condicao();
    }

    private sealed class BrokerIndisponivelFactory(FakeHubCatalogoClient hubCatalogoClient, FakeLogger<RelayOutboxJob> relayJobLogger)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHubCatalogoClient>();
                services.AddSingleton<IHubCatalogoClient>(hubCatalogoClient);
                services.RemoveAll<ILogger<RelayOutboxJob>>();
                services.AddSingleton<ILogger<RelayOutboxJob>>(relayJobLogger);
            });
        }
    }
}
