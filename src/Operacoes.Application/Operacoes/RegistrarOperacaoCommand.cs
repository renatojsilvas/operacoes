using MediatR;
using Operacoes.Domain.Common;

namespace Operacoes.Application.Operacoes;

public sealed record RegistrarOperacaoCommand(
    string ClienteId,
    string InstrumentoId,
    string Tipo,
    decimal Quantidade,
    decimal ValorFinanceiro,
    DateOnly DataEvento,
    string? EstornaOperacaoId,
    decimal? ValorOrigemSaldo,
    string IdempotencyKey) : IRequest<Result<RegistrarOperacaoResultado>>;

public sealed record RegistrarOperacaoResultado(
    string Id,
    string ClienteId,
    string InstrumentoId,
    string Tipo,
    decimal Quantidade,
    decimal ValorFinanceiro,
    DateOnly DataEvento,
    DateTimeOffset RegistradoEm,
    string? EstornaOperacaoId,
    decimal? ValorOrigemSaldo,
    bool Replay);
