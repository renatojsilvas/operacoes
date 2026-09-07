using MediatR;
using Operacoes.Application.Catalogo;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;

namespace Operacoes.Application.Instrumentos;

public sealed class ListarInstrumentosQueryHandler(
    IHubCatalogoClient hubCatalogoClient,
    IOperacaoReadRepository operacaoReadRepository)
    : IRequestHandler<ListarInstrumentosQuery, Result<ListarInstrumentosResultado>>
{
    private const int LimiteDefault = 20;
    private const int LimiteMaximo = 50;

    public async Task<Result<ListarInstrumentosResultado>> Handle(
        ListarInstrumentosQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return RequisicaoErrors.InstrumentosQueryObrigatoria;
        }

        var limiteResult = ResolverLimite(request.Limit);
        if (limiteResult.IsFailure)
        {
            return limiteResult.Error;
        }

        var query = request.Query.Trim();

        var catalogoResult = await hubCatalogoClient.BuscarPorTermoAsync(query, ct);
        if (catalogoResult.IsFailure)
        {
            return catalogoResult.Error;
        }

        var itens = catalogoResult.Value;

        HashSet<string>? negociados = null;

        if (!string.IsNullOrWhiteSpace(request.ClienteId))
        {
            var negociadosResult = await operacaoReadRepository.ObterInstrumentosNegociadosAsync(
                request.ClienteId.Trim(), ct);

            if (negociadosResult.IsFailure)
            {
                return negociadosResult.Error;
            }

            negociados = negociadosResult.Value.ToHashSet(StringComparer.Ordinal);
        }

        var filtrados = itens
            .Where(item => request.IncluirVencidos || !item.Vencido || CatalogoMatch.EhExato(item, query))
            .ToList();

        var totalCount = filtrados.Count;

        var itensDaPagina = filtrados
            .OrderByDescending(item => CatalogoMatch.EhExato(item, query))
            .ThenByDescending(item => negociados is not null && negociados.Contains(item.Id))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(limiteResult.Value)
            .Select(item => new InstrumentoAutocompleteItem(
                item.Id,
                item.Classe,
                item.NomeExibicao,
                item.Vencido,
                negociados is null ? null : negociados.Contains(item.Id)))
            .ToList();

        return new ListarInstrumentosResultado(itensDaPagina, totalCount);
    }

    private static Result<int> ResolverLimite(int? limit)
    {
        if (limit is null)
        {
            return LimiteDefault;
        }

        if (limit <= 0)
        {
            return RequisicaoErrors.InstrumentosLimitInvalido;
        }

        return Math.Min(limit.Value, LimiteMaximo);
    }
}
