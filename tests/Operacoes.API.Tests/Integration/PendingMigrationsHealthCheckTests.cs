using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

// PADROES.md §10.18: AddDbContextCheck<T> só prova CanConnectAsync(), não prova schema. Este teste
// prova o check adicional (PendingMigrationsHealthCheck) contra um Postgres real, simulando
// pendência de forma honesta: aplica só a primeira migration (InitialCreate) via IMigrator,
// deixando CriaSchemaOperacoes de fora de propósito, confere que /health/ready reprova, e só então
// aplica o resto e confere que aprova. Ambiente "Testing" porque é o único em que o
// DatabaseInitializer não migra sozinho no boot (ver DatabaseInitializer.cs) — sem isso, o boot já
// aplicaria tudo e não haveria pendência para observar.
public sealed class PendingMigrationsHealthCheckTests
{
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string InitialCreateMigrationId = "20260905164053_InitialCreate";

    [Fact]
    public async Task HealthReady_ComMigrationPendente_RespondeUnhealthy_EDepoisDeAplicarRespondeHealthy()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, postgres.GetConnectionString());
        try
        {
            await using var factory = new TestingBootFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.GetService<IMigrator>();

                // Aplica só até a primeira migration, deixando CriaSchemaOperacoes pendente de propósito.
                await migrator.MigrateAsync(InitialCreateMigrationId);
            }

            using var client = factory.CreateClient();
            var pendingResponse = await client.GetAsync("/health/ready", CancellationToken.None);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, pendingResponse.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
            }

            var okResponse = await client.GetAsync("/health/ready", CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }
    }

    // PADROES.md §10.18, o segundo risco nomeado (defeito B do revisor): GetPendingMigrationsAsync()
    // sozinho não detecta drift manual — o revisor provou isso dropando `operacoes` por fora com
    // `__EFMigrationsHistory` intacta e o readiness respondeu 200. Este teste reproduz exatamente
    // esse cenário contra um Postgres real e próprio (não o compartilhado da ApiTestFactory, para não
    // contaminar as outras suítes com uma tabela dropada no meio da run).
    [Fact]
    public async Task HealthReady_ComTabelaDropadaPorFora_RespondeUnhealthy()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, postgres.GetConnectionString());
        try
        {
            await using var factory = new TestingBootFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            var okResponse = await client.GetAsync("/health/ready", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Drift manual: tabela dropada por fora, __EFMigrationsHistory permanece intacta —
                // GetPendingMigrationsAsync() sozinho continuaria dizendo "zero pendências".
                await db.Database.ExecuteSqlRawAsync("DROP TABLE operacoes CASCADE;");
            }

            var driftResponse = await client.GetAsync("/health/ready", CancellationToken.None);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, driftResponse.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }
    }

    // PADROES.md §10.18/§10.22, defeito da segunda revisão adversarial: dropar `trg_operacoes_imutavel`
    // por fora (com `__EFMigrationsHistory` e a tabela `operacoes` intactas) deixava /health/ready em
    // 200 e o UPDATE/DELETE que antes falhava passava a funcionar em silêncio — a única guarda que
    // impede corrupção irreversível em `operacoes` sumia sem ninguém notar. Este teste reproduz esse
    // cenário exato contra um Postgres real e próprio (não o compartilhado da ApiTestFactory, mesmo
    // racional do teste de tabela dropada acima), nos dois sentidos: 200 antes, drop da trigger, 503
    // depois.
    [Fact]
    public async Task HealthReady_ComTriggerDeImutabilidadeDropadaPorFora_RespondeUnhealthy()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, postgres.GetConnectionString());
        try
        {
            await using var factory = new TestingBootFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            var okResponse = await client.GetAsync("/health/ready", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Drift manual: trigger dropada por fora, tabela e __EFMigrationsHistory permanecem
                // intactas — GetPendingMigrationsAsync() e a sonda de tabelas ausentes continuariam
                // dizendo "tudo certo".
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER trg_operacoes_imutavel ON operacoes;");
            }

            var driftResponse = await client.GetAsync("/health/ready", CancellationToken.None);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, driftResponse.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }
    }

    private sealed class TestingBootFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }
}
