using Microsoft.EntityFrameworkCore;
using Npgsql;
using Operacoes.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Operacoes.API.Tests.Integration;

public sealed class OutboxPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        DataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        Options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(DataSource).Options;

        await using var migrationDb = new AppDbContext(Options);
        await migrationDb.Database.MigrateAsync();
    }

    public async Task LimparAsync()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync();
        await using var comando = connection.CreateCommand();
        comando.CommandText = "TRUNCATE TABLE outbox RESTART IDENTITY CASCADE;";
        await comando.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("outbox-postgres")]
public sealed class OutboxPostgresCollection : ICollectionFixture<OutboxPostgresFixture>;
