using Operacoes.Domain.Common;

namespace Operacoes.Domain.Operacoes;

public static class OperacaoErrors
{
    public static readonly Error IdVazio =
        new("Operacao.IdVazio", "Id não deve ser vazio.");
    public static readonly Error ClienteIdVazio =
        new("Operacao.ClienteIdVazio", "ClienteId não deve ser vazio.");
    public static readonly Error InstrumentoIdVazio =
        new("Operacao.InstrumentoIdVazio", "InstrumentoId não deve ser vazio.");
    public static readonly Error TipoInvalido =
        new("TipoOperacao.Invalido", "Tipo de operação inválido.", ErrorType.Unprocessable);
    public static readonly Error EstornoAutoReferente =
        new(
            "Operacao.EstornoAutoReferente",
            "Uma operação não pode ser o estorno de si mesma.",
            ErrorType.Unprocessable);
    public static readonly Error EstornoIncoerente =
        new(
            "Operacao.EstornoIncoerente",
            "EstornaOperacaoId deve ser preenchido se, e somente se, o tipo for estorno.",
            ErrorType.Unprocessable);
    public static readonly Error EstornoReferenciaVazia =
        new(
            "Operacao.EstornoReferenciaVazia",
            "EstornaOperacaoId não deve ser vazio ou conter apenas espaços; use null para indicar ausência de referência.",
            ErrorType.Unprocessable);
    public static readonly Error QuantidadeInvalida =
        new("Operacao.QuantidadeInvalida", "Quantidade deve ser maior que zero.", ErrorType.Unprocessable);
    public static readonly Error ValorFinanceiroInvalido =
        new(
            "Operacao.ValorFinanceiroInvalido",
            "ValorFinanceiro deve ser maior que zero.",
            ErrorType.Unprocessable);
    public static readonly Error QuantidadeExcedePrecisaoSuportada =
        new(
            "Operacao.QuantidadeExcedePrecisaoSuportada",
            $"Quantidade deve ter no máximo {OperacaoNumericLimits.QuantidadePrecisao - OperacaoNumericLimits.QuantidadeEscala} " +
            $"dígitos inteiros e {OperacaoNumericLimits.QuantidadeEscala} dígitos decimais.",
            ErrorType.Unprocessable);
    public static readonly Error ValorFinanceiroExcedePrecisaoSuportada =
        new(
            "Operacao.ValorFinanceiroExcedePrecisaoSuportada",
            $"ValorFinanceiro deve ter no máximo {OperacaoNumericLimits.ValorFinanceiroPrecisao - OperacaoNumericLimits.ValorFinanceiroEscala} " +
            $"dígitos inteiros e {OperacaoNumericLimits.ValorFinanceiroEscala} dígitos decimais.",
            ErrorType.Unprocessable);
    public static readonly Error DataEventoFutura =
        new(
            "Operacao.DataEventoFutura",
            "DataEvento não pode ser posterior a hoje.",
            ErrorType.Unprocessable);
    public static readonly Error InstrumentoInexistente =
        new(
            "Operacao.InstrumentoInexistente",
            "Instrumento não encontrado no catálogo do Hub.",
            ErrorType.Unprocessable);
    public static readonly Error EstornoReferenciaInvalida =
        new(
            "Operacao.EstornoReferenciaInvalida",
            "A operação referenciada por EstornaOperacaoId não existe, ou não pertence ao mesmo " +
            "cliente e instrumento desta operação.",
            ErrorType.Unprocessable);
    public static readonly Error EstornoJaRealizado =
        new(
            "Operacao.EstornoJaRealizado",
            "Esta operação já foi estornada.",
            ErrorType.Conflict);
    public static readonly Error ValorOrigemSaldoIncoerente =
        new(
            "Operacao.ValorOrigemSaldoIncoerente",
            "ValorOrigemSaldo deve ser preenchido se, e somente se, o tipo for aplicacao ou aporte.",
            ErrorType.Unprocessable);
    public static readonly Error ValorOrigemSaldoInvalido =
        new(
            "Operacao.ValorOrigemSaldoInvalido",
            "ValorOrigemSaldo não pode ser negativo.",
            ErrorType.Unprocessable);
    public static readonly Error ValorOrigemSaldoExcedeValorFinanceiro =
        new(
            "Operacao.ValorOrigemSaldoExcedeValorFinanceiro",
            "ValorOrigemSaldo não pode ser maior que ValorFinanceiro.",
            ErrorType.Unprocessable);
    public static readonly Error ValorOrigemSaldoExcedePrecisaoSuportada =
        new(
            "Operacao.ValorOrigemSaldoExcedePrecisaoSuportada",
            $"ValorOrigemSaldo deve ter no máximo {OperacaoNumericLimits.ValorFinanceiroPrecisao - OperacaoNumericLimits.ValorFinanceiroEscala} " +
            $"dígitos inteiros e {OperacaoNumericLimits.ValorFinanceiroEscala} dígitos decimais.",
            ErrorType.Unprocessable);
}
