using Microsoft.Extensions.Caching.Memory;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure.Caching;

namespace Operacoes.Infrastructure.Tests.Caching;

public sealed class MemoryCacheResultExtensionsTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10 });

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetOrCreateResultAsync_ComCacheVazio_ChamaAFactory()
    {
        var chamadas = 0;

        Task<Result<string>> Factory()
        {
            chamadas++;
            return Task.FromResult(Result<string>.Success("valor"));
        }

        var resultado = await _cache.GetOrCreateResultAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);

        Assert.True(resultado.IsSuccess);
        Assert.Equal("valor", resultado.Value);
        Assert.Equal(1, chamadas);
    }

    [Fact]
    public async Task GetOrCreateResultAsync_ComEntradaJaCacheada_NaoChamaAFactoryDeNovo()
    {
        var chamadas = 0;

        Task<Result<string>> Factory()
        {
            chamadas++;
            return Task.FromResult(Result<string>.Success("valor"));
        }

        await _cache.GetOrCreateResultAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);
        await _cache.GetOrCreateResultAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);

        Assert.Equal(1, chamadas);
    }

    [Fact]
    public async Task GetOrCreateResultAsync_ComFalhaDaFactory_NaoCacheiaEChamaDeNovoNaProximaVez()
    {
        var chamadas = 0;

        Task<Result<string>> Factory()
        {
            chamadas++;
            return Task.FromResult(Result<string>.Failure(new Error("Fonte.Indisponivel", "falha", ErrorType.Unavailable)));
        }

        var primeiro = await _cache.GetOrCreateResultAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);
        var segundo = await _cache.GetOrCreateResultAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);

        Assert.True(primeiro.IsFailure);
        Assert.True(segundo.IsFailure);
        Assert.Equal(2, chamadas);
    }

    [Fact]
    public async Task GetOrCreateResultAsync_ComCacheDeTamanhoLimitado_NaoLancaPorqueAEntradaDeclaraSize()
    {
        using var cacheLimitado = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1 });

        var excecao = await Record.ExceptionAsync(() => cacheLimitado.GetOrCreateResultAsync(
            "chave",
            TimeSpan.FromMinutes(1),
            CancellationToken.None,
            () => Task.FromResult(Result<string>.Success("valor"))));

        Assert.Null(excecao);
    }
}
