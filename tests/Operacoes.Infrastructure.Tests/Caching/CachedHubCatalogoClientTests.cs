using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure.Caching;

namespace Operacoes.Infrastructure.Tests.Caching;

public sealed class CachedHubCatalogoClientTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache;

    public CachedHubCatalogoClientTests()
    {
        _cache = FakeMemoryCacheFactory.ComRelogioControlavel(_timeProvider, sizeLimit: 1_000);
    }

    public void Dispose() => _cache.Dispose();

    private static IConfiguration BuildConfiguration(string? ttl = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:CatalogoInstrumentos"] = ttl,
            })
            .Build();

    private static InstrumentoCatalogo Item(string id) => new(id, "titulo-publico", id, Vencido: false);

    [Fact]
    public async Task BuscarPorTermoAsync_ChamadoDuasVezesComOMesmoTermoDentroDoTtl_ChamaOInnerUmaVezSo()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success([Item("td:tesouro-selic-2029")]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);
        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.Single(inner.Chamadas);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ChamadoAposOTtlExpirar_VoltaAChamarOInner()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success([Item("td:tesouro-selic-2029")]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);
        _timeProvider.Advance(Ttl + TimeSpan.FromSeconds(1));
        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.Equal(2, inner.Chamadas.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComHubEmFalha_NaoCacheiaEAChamadaSeguinteVoltaAoHub()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Failure(CatalogoErrors.HubIndisponivel),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        var primeiro = await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);
        var segundo = await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(primeiro.IsFailure);
        Assert.True(segundo.IsFailure);
        Assert.Equal(2, inner.Chamadas.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComTermosDeCaixaDiferente_UsaChavesDiferentes()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success([Item("td:tesouro-selic-2029")]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);
        await sut.BuscarPorTermoAsync("TESOURO", CancellationToken.None);

        Assert.Equal(2, inner.Chamadas.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComTtlZero_FazBypassEChamaOInnerACadaVez()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success([Item("td:tesouro-selic-2029")]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration("00:00:00"));

        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);
        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.Equal(2, inner.Chamadas.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ChaveDeCacheContemOTermoENaoContemNadaDeCliente()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success([Item("td:tesouro-selic-2029")]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        await sut.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(_cache.TryGetValue("catalogo:instrumentos:tesouro", out var cacheado));
        var itens = Assert.IsAssignableFrom<IReadOnlyList<InstrumentoCatalogo>>(cacheado);
        Assert.Single(itens);
        Assert.False(_cache.TryGetValue("catalogo:instrumentos:tesouro:cliente-1", out _));
    }

    [Fact]
    public async Task BuscarPorTermoAsync_CacheiaOSnapshotCruSemFiltroDeVencido()
    {
        var inner = new FakeHubCatalogoClient
        {
            Resposta = Result<IReadOnlyList<InstrumentoCatalogo>>.Success(
                [new InstrumentoCatalogo("td:vencido", "titulo-publico", "Vencido", Vencido: true)]),
        };
        var sut = new CachedHubCatalogoClient(inner, _cache, BuildConfiguration(Ttl.ToString()));

        var resultado = await sut.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value);
        Assert.True(item.Vencido);
    }
}
