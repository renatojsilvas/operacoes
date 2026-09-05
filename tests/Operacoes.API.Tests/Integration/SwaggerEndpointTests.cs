using System.Net;
using System.Text.Json;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class SwaggerEndpointTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSwaggerJson_ShouldReturn200()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSwaggerJson_ShouldReturnValidOpenApiDocument()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out _), $"documento deveria declarar 'openapi'.\n{body}");
        Assert.True(root.TryGetProperty("info", out _), $"documento deveria declarar 'info'.\n{body}");
        Assert.True(root.TryGetProperty("paths", out _), $"documento deveria declarar 'paths'.\n{body}");
    }

    [Fact]
    public async Task GetSwaggerJson_ShouldExposeNoBusinessPaths()
    {
        // Divergência deliberada do molde (hub-precos): este F1 não tem endpoint de negócio (ver
        // docs/ROADMAP.md, grupo /v1 nasce no F3). O controle aqui é o inverso do hub: prova que o
        // documento gerado é válido e vazio de paths, não que expõe uma lista fixa conhecida.
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");

        var pathNames = paths.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Empty(pathNames);
    }

    [Fact]
    public async Task GetSwaggerJson_ShouldDescribeApiKeySecurityScheme()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        var apiKeyScheme = securitySchemes.GetProperty("ApiKey");

        Assert.Equal("apiKey", apiKeyScheme.GetProperty("type").GetString());
        Assert.Equal("header", apiKeyScheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", apiKeyScheme.GetProperty("name").GetString());

        Assert.True(root.TryGetProperty("security", out var documentSecurity),
            $"o documento deveria ter um security requirement global referenciando ApiKey.\n{body}");
        var referencesApiKeyScheme = documentSecurity.EnumerateArray()
            .Any(requirement => requirement.TryGetProperty("ApiKey", out var apiKeyRequirement)
                && apiKeyRequirement.ValueKind == JsonValueKind.Array);
        Assert.True(referencesApiKeyScheme,
            $"o security requirement global deveria referenciar o scheme ApiKey.\n{documentSecurity}");
    }
}
