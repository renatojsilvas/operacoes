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
    public async Task GetSwaggerJson_ShouldExposeOnlyOperacoesPostPath()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        var paths = document.RootElement.GetProperty("paths");
        var pathNames = paths.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["/v1/operacoes"], pathNames);
        Assert.True(
            paths.GetProperty("/v1/operacoes").TryGetProperty("post", out _),
            $"/v1/operacoes deveria expor POST.\n{body}");
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
