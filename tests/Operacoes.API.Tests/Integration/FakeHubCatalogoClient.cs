using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.API.Tests.Integration;

public sealed class FakeHubCatalogoClient : IHubCatalogoClient
{
    public Result<bool> Resposta { get; set; } = Result<bool>.Success(true);

    public List<string> Chamadas { get; } = [];

    public Task<Result<bool>> InstrumentoExisteAsync(string instrumentoId, CancellationToken ct)
    {
        Chamadas.Add(instrumentoId);
        return Task.FromResult(Resposta);
    }
}
