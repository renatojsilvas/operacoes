using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Operacoes.Infrastructure.Caching;
using Operacoes.Infrastructure.Tests.Http;

namespace Operacoes.Infrastructure.Tests.Caching;

public sealed class CachedContentVersionProviderTests : IDisposable
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetVersionAsync_ChamadoDuasVezes_ChamaOInnerUmaVezSo()
    {
        var inner = new FakeContentVersionProvider("2026-09-06-1");
        var sut = new CachedContentVersionProvider(inner, _cache, new ConfigurationBuilder().Build());

        var primeiro = await sut.GetVersionAsync(CancellationToken.None);
        var segundo = await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal(primeiro, segundo);
        Assert.Equal(1, inner.Chamadas);
    }

    [Fact]
    public async Task GetVersionAsync_ComCacheMiss_DevolveAVersaoProduzidaPeloInner()
    {
        var inner = new FakeContentVersionProvider("2026-09-06-1");
        var sut = new CachedContentVersionProvider(inner, _cache, new ConfigurationBuilder().Build());

        var version = await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal("2026-09-06-1", version);
    }

    [Fact]
    public async Task GetVersionAsync_ComTtlZero_FazBypassEChamaOInnerACadaVez()
    {
        var inner = new FakeContentVersionProvider("2026-09-06-1");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:ContentVersion"] = "00:00:00" })
            .Build();
        var sut = new CachedContentVersionProvider(inner, _cache, config);

        await sut.GetVersionAsync(CancellationToken.None);
        await sut.GetVersionAsync(CancellationToken.None);

        Assert.Equal(2, inner.Chamadas);
    }

    [Fact]
    public async Task GetVersionAsync_ComCacheDeTamanhoLimitado_NaoLancaPorqueAEntradaDeclaraSize()
    {
        using var cacheLimitado = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var inner = new FakeContentVersionProvider("2026-09-06-1");
        var sut = new CachedContentVersionProvider(inner, cacheLimitado, new ConfigurationBuilder().Build());

        var excecao = await Record.ExceptionAsync(() => sut.GetVersionAsync(CancellationToken.None));

        Assert.Null(excecao);
    }
}
