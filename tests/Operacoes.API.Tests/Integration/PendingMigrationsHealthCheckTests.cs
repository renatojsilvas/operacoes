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
