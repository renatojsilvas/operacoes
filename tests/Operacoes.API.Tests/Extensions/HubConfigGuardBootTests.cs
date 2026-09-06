using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Operacoes.API.Tests.Extensions;

public sealed class HubConfigGuardBootTests
{
    [Fact]
    public void Boot_Production_WithEmptyHubBaseUrl_ShouldFailToStartWithMessageContainingHubBaseUrlName()
    {
        using var factory = new ProductionWithoutHubConfigFactory(baseUrl: "", apiKey: "valid-hub-api-key-with-32-chars-or-more");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Hub:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_Production_WithEmptyHubApiKey_ShouldFailToStartWithMessageContainingHubApiKeyName()
    {
        using var factory = new ProductionWithoutHubConfigFactory(
            baseUrl: "http://hub-precos-app:8080/", apiKey: "");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Hub:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_Production_WithHubApiKeyBelowMinLength_ShouldFailToStartWithMessageAboutMinimum()
    {
        using var factory = new ProductionWithoutHubConfigFactory(
            baseUrl: "http://hub-precos-app:8080/", apiKey: new string('a', 31));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Hub:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("abaixo do mínimo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_Production_WithHubApiKeyAsBlockedPlaceholder_ShouldFailToStartWithMessageAboutPlaceholder()
    {
        using var factory = new ProductionWithoutHubConfigFactory(
            baseUrl: "http://hub-precos-app:8080/", apiKey: "CHANGE-ME-IN-PRODUCTION-padded-xxxx");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Hub:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ProductionWithoutHubConfigFactory(string baseUrl, string apiKey)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=fake;Username=fake;Password=fake",
                    ["ApiKey:Key"] = new string('a', 64),
                    ["Hub:BaseUrl"] = baseUrl,
                    ["Hub:ApiKey"] = apiKey,
                });
            });
        }
    }
}
