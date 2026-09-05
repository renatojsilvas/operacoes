using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Operacoes.API.Tests.Integration;

// Substitui EfRoundTripTests/SchemaTests do molde (hub-precos): não há entidade de negócio neste F1
// (ver docs/ROADMAP.md), então não há o que fazer round-trip nem coluna/índice para inspecionar. O que
// tem que ficar provado é o coração do F1: migrations aplicando sozinhas NO BOOT, contra um Postgres
// real — não a chamada manual a MigrateAsync que o ApiTestFactory (ambiente "Testing") faz, porque o
// DatabaseInitializer pula a inicialização exatamente nesse ambiente. Por isso este teste sobe a
// aplicação inteira (Program.cs) em "Development" — onde a guarda de API key é isenta mas o
// DatabaseInitializer roda de verdade — contra um Testcontainers próprio, e confirma a linha da
// migration em "__EFMigrationsHistory".
//
// A connection string de teste é injetada via variável de ambiente (não via
// WebApplicationFactory.ConfigureAppConfiguration): AddInfrastructure lê e fixa a connection string
// dentro do NpgsqlDataSource ANTES de builder.Build() (Program.cs), e é só no Build() que o hook de
// configuração da WebApplicationFactory (WebApplicationFactory usa DeferredHostBuilder para apps de
// hosting mínimo) tem chance de agir — tarde demais para esse valor específico. A variável de ambiente,
// por já existir no processo antes de WebApplication.CreateBuilder(args) rodar, chega a tempo. Mesmo
// padrão do ApiTestFactory e do ApiKeyNormalizationTests.ProductionApiKeyFactory deste projeto.
public sealed class MigrationsBootTests
{
    private const string MigrationId = "20260905164053_InitialCreate";
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";

    [Fact]
    public async Task Boot_ComPostgresReal_AplicaMigrationsERegistraALinhaEmEfMigrationsHistory()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, connectionString);
        try
        {
            await using var factory = new DevelopmentBootFactory();

            // Acessar Services força o host a subir por completo — inclusive o Program.cs top-level,
            // que chama app.InitializeDatabaseAsync() antes de app.Run(). Se a migration falhar, lança.
            _ = factory.Services;
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" = @migrationId
            """;
        command.Parameters.AddWithValue("migrationId", MigrationId);

        var count = (long)(await command.ExecuteScalarAsync())!;

        Assert.True(
            count == 1,
            $"esperava exatamente 1 linha para a migration '{MigrationId}' em __EFMigrationsHistory " +
            $"após o boot (aplicada automaticamente pelo DatabaseInitializer), encontrado count={count}. " +
            "Isso prova migrations-no-boot (o coração do F1), não uma chamada manual a MigrateAsync.");
    }

    [Fact]
    public async Task Boot_ComPostgresReal_HealthReadyRespondeOkAposMigrar()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, postgres.GetConnectionString());
        try
        {
            await using var factory = new DevelopmentBootFactory();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health/ready", CancellationToken.None);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }
    }

    private sealed class DevelopmentBootFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }
}
