using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.API.Tests.Integration;

public sealed class FakeHubCatalogoClient : IHubCatalogoClient
{
    public Result<bool> Resposta { get; set; } = Result<bool>.Success(true);

    public IReadOnlyList<InstrumentoCatalogo>? Catalogo { get; set; }

    public List<string> Chamadas { get; } = [];

    public void Reset()
    {
        Resposta = Result<bool>.Success(true);
        Catalogo = null;
        Chamadas.Clear();
    }

    public Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct)
    {
        Chamadas.Add(termo);

        if (Resposta.IsFailure)
        {
            return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Failure(Resposta.Error));
        }

        if (Catalogo is not null)
        {
            IReadOnlyList<InstrumentoCatalogo> encontrados = Catalogo
                .Where(item =>
                    (item.Id?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    item.NomeExibicao.Contains(termo, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Success(encontrados));
        }

        IReadOnlyList<InstrumentoCatalogo> itens = Resposta.Value
            ? [new InstrumentoCatalogo(termo, "titulo-publico", termo, Vencido: false)]
            : [];

        return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Success(itens));
    }
}
