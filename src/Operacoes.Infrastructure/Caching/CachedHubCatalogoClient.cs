using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.Infrastructure.Caching;

public sealed class CachedHubCatalogoClient(
    IHubCatalogoClient inner,
    IMemoryCache cache,
    IConfiguration configuration) : IHubCatalogoClient
{
    private const string CacheKeyPrefix = "catalogo:instrumentos:";
    private const string TtlConfigKey = "Caching:CatalogoInstrumentos";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    public Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct)
    {
        var ttl = GetTtl();

        if (ttl is null)
        {
            return inner.BuscarPorTermoAsync(termo, ct);
        }

        var chave = CacheKeyPrefix + termo.Trim();

        return cache.GetOrCreateResultAsync(chave, ttl.Value, CancellationToken.None, () => inner.BuscarPorTermoAsync(termo, ct));
    }

    private TimeSpan? GetTtl()
    {
        var configurado = configuration.GetValue<TimeSpan?>(TtlConfigKey);

        if (configurado is { Ticks: <= 0 })
        {
            return null;
        }

        return configurado is { Ticks: > 0 } ttl ? ttl : DefaultTtl;
    }
}
