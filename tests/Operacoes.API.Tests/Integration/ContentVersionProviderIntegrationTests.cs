using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Operacoes.Infrastructure.Http;

namespace Operacoes.API.Tests.Integration;

[Collection("outbox-postgres")]
public sealed class ContentVersionProviderIntegrationTests(OutboxPostgresFixture fixture)
{
    private static readonly TimeSpan CatalogoTtl = TimeSpan.FromSeconds(10);

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:CatalogoInstrumentos"] = CatalogoTtl.ToString(),
            })
            .Build();

    private static DateTimeOffset AlinhadoAoBalde(DateTimeOffset instante)
    {
        var ticksAlinhados = instante.Ticks / CatalogoTtl.Ticks * CatalogoTtl.Ticks;
        return new DateTimeOffset(ticksAlinhados, TimeSpan.Zero);
    }

    [Fact]
    public async Task GetVersionAsync_ComDadoLocalInalterado_QuandoORelogioAvancaAlemDoTtl_MudaAVersao()
    {
        var inicioDoBalde = AlinhadoAoBalde(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
        var timeProvider = new FakeTimeProvider(inicioDoBalde);
        var provider = new ContentVersionProvider(fixture.DataSource, timeProvider, BuildConfiguration());

        var versaoAntes = await provider.GetVersionAsync(CancellationToken.None);

        timeProvider.SetUtcNow(inicioDoBalde + CatalogoTtl + TimeSpan.FromTicks(1));
        var versaoDepois = await provider.GetVersionAsync(CancellationToken.None);

        Assert.NotEqual(versaoAntes, versaoDepois);
    }

    [Fact]
    public async Task GetVersionAsync_ComDadoLocalInalterado_DentroDoMesmoBalde_MantemAMesmaVersao()
    {
        var inicioDoBalde = AlinhadoAoBalde(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
        var timeProvider = new FakeTimeProvider(inicioDoBalde);
        var provider = new ContentVersionProvider(fixture.DataSource, timeProvider, BuildConfiguration());

        var primeira = await provider.GetVersionAsync(CancellationToken.None);

        timeProvider.SetUtcNow(inicioDoBalde + TimeSpan.FromTicks(CatalogoTtl.Ticks / 2));
        var segunda = await provider.GetVersionAsync(CancellationToken.None);

        Assert.Equal(primeira, segunda);
    }

    [Fact]
    public async Task GetVersionAsync_ComCatalogoTtlZerado_NaoAfirmaJanelaEVariaACadaLeitura()
    {
        var configuracaoComCacheDesligado = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:CatalogoInstrumentos"] = "00:00:00",
            })
            .Build();
        var instante = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(instante);
        var provider = new ContentVersionProvider(fixture.DataSource, timeProvider, configuracaoComCacheDesligado);

        var primeira = await provider.GetVersionAsync(CancellationToken.None);

        timeProvider.SetUtcNow(instante + TimeSpan.FromMilliseconds(1));
        var segunda = await provider.GetVersionAsync(CancellationToken.None);

        Assert.NotEqual(primeira, segunda);
    }
}
