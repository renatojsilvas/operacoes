using Operacoes.Domain.Common;

namespace Operacoes.Application.Catalogo;

public interface IHubCatalogoClient
{
    Task<Result<bool>> InstrumentoExisteAsync(string instrumentoId, CancellationToken ct);
}
