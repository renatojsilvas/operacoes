using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

public sealed class AdicionaValorOrigemSaldoOperacoesMigrationTests
{
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string MigrationAnteriorId = "20260906234331_AdicionaIndiceOperacoesClienteInstrumento";
    private const string MigrationAlvoId = "AdicionaValorOrigemSaldoOperacoes";

    [Fact]
    public async Task Migrate_ComLinhaDeAporteExistenteSemValorOrigemSaldo_FalhaComMensagemLegivel()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, connectionString);
        try
        {
            await using var factory = new TestingBootFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.GetService<IMigrator>();

                await migrator.MigrateAsync(MigrationAnteriorId);
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento)
                    VALUES ('op-preexistente', 'cliente-1', 'td:tesouro-selic-2029', 'aporte', 10.5, 1000.00, '2026-01-01')
                    """;
                await command.ExecuteNonQueryAsync();
            }

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.GetService<IMigrator>();

                var exception = await Record.ExceptionAsync(() => migrator.MigrateAsync());

                Assert.NotNull(exception);
                Assert.Contains(MigrationAlvoId, exception!.ToString());
                Assert.Contains("1 linha", exception.ToString());
                Assert.Contains("append-only", exception.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        }
    }

    [Fact]
    public async Task Migrate_ComBaseSemLinhaDeAplicacaoOuAporte_AplicaNormalmente()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, connectionString);
        try
        {
            await using var factory = new TestingBootFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.GetService<IMigrator>();

                await migrator.MigrateAsync(MigrationAnteriorId);
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento)
                    VALUES ('op-resgate-preexistente', 'cliente-1', 'td:tesouro-selic-2029', 'resgate', 10.5, 1000.00, '2026-01-01')
                    """;
                await command.ExecuteNonQueryAsync();
            }

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrator = db.GetService<IMigrator>();

                var exception = await Record.ExceptionAsync(() => migrator.MigrateAsync());

                Assert.Null(exception);
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%' || @migrationId
                    """;
                command.Parameters.AddWithValue("migrationId", MigrationAlvoId);

                var count = (long)(await command.ExecuteScalarAsync())!;

                Assert.Equal(1, count);
            }
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
