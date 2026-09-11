using Operacoes.Domain.Common;

namespace Operacoes.Domain.Operacoes;

public sealed class Operacao : Entity<string>
{
    private Operacao(
        string id,
        string clienteId,
        string instrumentoId,
        TipoOperacao tipo,
        decimal quantidade,
        decimal valorFinanceiro,
        DateOnly dataEvento,
        DateTimeOffset registradoEm,
        string? estornaOperacaoId,
        decimal? valorOrigemSaldo)
        : base(id)
    {
        ClienteId = clienteId;
        InstrumentoId = instrumentoId;
        Tipo = tipo;
        Quantidade = quantidade;
        ValorFinanceiro = valorFinanceiro;
        DataEvento = dataEvento;
        RegistradoEm = registradoEm;
        EstornaOperacaoId = estornaOperacaoId;
        ValorOrigemSaldo = valorOrigemSaldo;
    }

    public string ClienteId { get; }
    public string InstrumentoId { get; }
    public TipoOperacao Tipo { get; }
    public decimal Quantidade { get; }
    public decimal ValorFinanceiro { get; }
    public DateOnly DataEvento { get; }
    public DateTimeOffset RegistradoEm { get; }
    public string? EstornaOperacaoId { get; }
    public decimal? ValorOrigemSaldo { get; }
    public static Result<Operacao> Create(
        string id,
        string clienteId,
        string instrumentoId,
        TipoOperacao tipo,
        decimal quantidade,
        decimal valorFinanceiro,
        DateOnly dataEvento,
        DateTimeOffset registradoEm,
        DateOnly hoje,
        string? estornaOperacaoId = null,
        decimal? valorOrigemSaldo = null)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        if (string.IsNullOrWhiteSpace(id))
        {
            return OperacaoErrors.IdVazio;
        }
        if (string.IsNullOrWhiteSpace(clienteId))
        {
            return OperacaoErrors.ClienteIdVazio;
        }
        if (string.IsNullOrWhiteSpace(instrumentoId))
        {
            return OperacaoErrors.InstrumentoIdVazio;
        }
        if (quantidade <= 0)
        {
            return OperacaoErrors.QuantidadeInvalida;
        }
        if (ExcedePrecisaoSuportada(quantidade, OperacaoNumericLimits.QuantidadeLimiteSuperiorExclusivo, OperacaoNumericLimits.QuantidadeEscala))
        {
            return OperacaoErrors.QuantidadeExcedePrecisaoSuportada;
        }
        if (valorFinanceiro <= 0)
        {
            return OperacaoErrors.ValorFinanceiroInvalido;
        }
        if (ExcedePrecisaoSuportada(valorFinanceiro, OperacaoNumericLimits.ValorFinanceiroLimiteSuperiorExclusivo, OperacaoNumericLimits.ValorFinanceiroEscala))
        {
            return OperacaoErrors.ValorFinanceiroExcedePrecisaoSuportada;
        }
        if (valorOrigemSaldo is not null)
        {
            if (ExcedePrecisaoSuportada(valorOrigemSaldo.Value, OperacaoNumericLimits.ValorFinanceiroLimiteSuperiorExclusivo, OperacaoNumericLimits.ValorFinanceiroEscala))
            {
                return OperacaoErrors.ValorOrigemSaldoExcedePrecisaoSuportada;
            }
            if (valorOrigemSaldo.Value < 0)
            {
                return OperacaoErrors.ValorOrigemSaldoInvalido;
            }
            if (valorOrigemSaldo.Value > valorFinanceiro)
            {
                return OperacaoErrors.ValorOrigemSaldoExcedeValorFinanceiro;
            }
        }
        var exigeValorOrigemSaldo = tipo == TipoOperacao.Aplicacao || tipo == TipoOperacao.Aporte;
        if (exigeValorOrigemSaldo != (valorOrigemSaldo is not null))
        {
            return OperacaoErrors.ValorOrigemSaldoIncoerente;
        }
        if (dataEvento > hoje)
        {
            return OperacaoErrors.DataEventoFutura;
        }
        if (estornaOperacaoId is not null && string.IsNullOrWhiteSpace(estornaOperacaoId))
        {
            return OperacaoErrors.EstornoReferenciaVazia;
        }
        var idNormalizado = id.Trim();
        var clienteIdNormalizado = clienteId.Trim();
        var instrumentoIdNormalizado = instrumentoId.Trim();
        var estornaOperacaoIdNormalizado = estornaOperacaoId?.Trim();
        if (estornaOperacaoIdNormalizado == idNormalizado)
        {
            return OperacaoErrors.EstornoAutoReferente;
        }
        var temReferenciaDeEstorno = estornaOperacaoIdNormalizado is not null;
        if ((tipo == TipoOperacao.Estorno) != temReferenciaDeEstorno)
        {
            return OperacaoErrors.EstornoIncoerente;
        }
        return new Operacao(
            idNormalizado, clienteIdNormalizado, instrumentoIdNormalizado, tipo, quantidade,
            valorFinanceiro, dataEvento, registradoEm, estornaOperacaoIdNormalizado, valorOrigemSaldo);
    }

    private static bool ExcedePrecisaoSuportada(decimal valor, decimal limiteSuperiorExclusivo, int escala) =>
        valor >= limiteSuperiorExclusivo || decimal.Round(valor, escala) != valor;
}
