using Operacoes.Domain.Common;

namespace Operacoes.Application.Catalogo;

public interface IHubCatalogoClient
{
    Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct);
}
