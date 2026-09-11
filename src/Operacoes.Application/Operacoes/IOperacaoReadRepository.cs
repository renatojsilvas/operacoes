using Operacoes.Domain.Common;

namespace Operacoes.Application.Operacoes;

public interface IOperacaoReadRepository
{

    Task<Result<bool>> ExisteComoReferenciaDeEstornoAsync(
        string estornaOperacaoId, string clienteId, string instrumentoId, CancellationToken ct);

    Task<Result<OperacaoConsulta>> ObterPorIdAsync(string id, CancellationToken ct);

    Task<Result<IReadOnlyList<string>>> ObterInstrumentosNegociadosAsync(string clienteId, CancellationToken ct);
}

public sealed record OperacaoRegistradaRow(
    string Id,
    string ClienteId,
    string InstrumentoId,
    string Operacao,
    decimal Quantidade,
    decimal ValorFinanceiro,
    DateOnly DataEvento,
    DateTimeOffset RegistradoEm,
    string? EstornaOperacaoId,
    decimal? ValorOrigemSaldo);

public sealed record OperacaoConsulta(bool Encontrada, OperacaoRegistradaRow? Linha)
{
    public static readonly OperacaoConsulta NaoEncontrada = new(false, null);

    public static OperacaoConsulta DeLinha(OperacaoRegistradaRow linha) => new(true, linha);
}
