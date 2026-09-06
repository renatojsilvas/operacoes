using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Operacoes.API.Tests.Integration;

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
