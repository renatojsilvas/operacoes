using MediatR;
using Operacoes.Domain.Common;

namespace Operacoes.Application.Instrumentos;

public sealed record ListarInstrumentosQuery(
    string? Query,
    string? ClienteId,
    bool IncluirVencidos,
    int? Limit) : IRequest<Result<ListarInstrumentosResultado>>;

public sealed record ListarInstrumentosResultado(IReadOnlyList<InstrumentoAutocompleteItem> Itens, int TotalCount);

public sealed record InstrumentoAutocompleteItem(
    string Id,
    string Classe,
    string NomeExibicao,
    bool Vencido,
    bool? JaNegociado);
