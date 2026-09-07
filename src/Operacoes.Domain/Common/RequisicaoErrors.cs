namespace Operacoes.Domain.Common;

public static class RequisicaoErrors
{
    public static readonly Error IdempotencyKeyAusente =
        new(
            "Requisicao.IdempotencyKeyAusente",
            "O header Idempotency-Key é obrigatório e não pode ser vazio.");

    public static readonly Error InstrumentosQueryObrigatoria =
        new(
            "Instrumentos.QueryObrigatoria",
            "O parâmetro query é obrigatório e não pode ser vazio ou conter apenas espaços.");

    public static readonly Error InstrumentosLimitInvalido =
        new(
            "Instrumentos.LimitInvalido",
            "O parâmetro limit deve ser maior que zero.");
}
