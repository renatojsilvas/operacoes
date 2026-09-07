using Microsoft.Extensions.Configuration;

namespace Operacoes.API.Tests.Configuration;

public sealed class AppSettingsTests
{
    private static IConfiguration CarregarAppSettingsJson() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

    [Fact]
    public void AppSettingsJson_HttpCacheControl_IgualAoDefaultDoConditionalGetFilter()
    {
        var configuracao = CarregarAppSettingsJson();

        Assert.Equal("private, max-age=60", configuracao["Http:CacheControl"]);
    }

    [Fact]
    public void AppSettingsJson_CachingCatalogoInstrumentos_ParseiaParaOMesmoTimeSpanDoDefaultDoCachedHubCatalogoClient()
    {
        var configuracao = CarregarAppSettingsJson();

        var valor = configuracao.GetValue<TimeSpan?>("Caching:CatalogoInstrumentos");

        Assert.Equal(TimeSpan.FromSeconds(60), valor);
    }

    [Fact]
    public void AppSettingsJson_CachingContentVersion_ParseiaParaOMesmoTimeSpanDoDefaultDoCachedContentVersionProvider()
    {
        var configuracao = CarregarAppSettingsJson();

        var valor = configuracao.GetValue<TimeSpan?>("Caching:ContentVersion");

        Assert.Equal(TimeSpan.FromSeconds(10), valor);
    }
}
