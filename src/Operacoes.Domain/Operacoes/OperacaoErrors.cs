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
        new("TipoOperacao.Invalido", "Tipo de operação inválido.");

    public static readonly Error EstornoAutoReferente =
        new("Operacao.EstornoAutoReferente", "Uma operação não pode ser o estorno de si mesma.");

    public static readonly Error EstornoIncoerente =
        new(
            "Operacao.EstornoIncoerente",
            "EstornaOperacaoId deve ser preenchido se, e somente se, o tipo for estorno.");

    public static readonly Error EstornoReferenciaVazia =
        new(
            "Operacao.EstornoReferenciaVazia",
            "EstornaOperacaoId não deve ser vazio ou conter apenas espaços; use null para indicar ausência de referência.");
}
