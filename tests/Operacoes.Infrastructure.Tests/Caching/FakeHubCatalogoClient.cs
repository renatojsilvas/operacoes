using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.Infrastructure.Tests.Caching;

internal sealed class FakeHubCatalogoClient : IHubCatalogoClient
{
    public Result<IReadOnlyList<InstrumentoCatalogo>> Resposta { get; set; } =
        Result<IReadOnlyList<InstrumentoCatalogo>>.Success(Array.Empty<InstrumentoCatalogo>());

    public List<string> Chamadas { get; } = [];

    public Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct)
    {
        Chamadas.Add(termo);
        return Task.FromResult(Resposta);
    }
}
