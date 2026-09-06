using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Operacoes.Application.Catalogo;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ValidApiKey = "integration-test-api-key-0123456789";
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string ApiKeyEnvVar = "ApiKey__Key";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();
    public FakeHubCatalogoClient HubCatalogoClient { get; } = new();
    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, ValidApiKey);
            using var scope = Services.CreateScope();

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

    public new async Task DisposeAsync()
    {
        try
        {
            await _postgres.DisposeAsync();
        }
        finally
        {
            ClearEnvironmentVariables();
            await base.DisposeAsync();
        }
    }

    private static void ClearEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, null);
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, ValidApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHubCatalogoClient>();
            services.AddSingleton<IHubCatalogoClient>(HubCatalogoClient);
        });
    }
}
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiTestFactory>;
