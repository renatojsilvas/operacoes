namespace Operacoes.Domain.Common;

public static class RequisicaoErrors
{
    public static readonly Error IdempotencyKeyAusente =
        new(
            "Requisicao.IdempotencyKeyAusente",
            "O header Idempotency-Key é obrigatório e não pode ser vazio.");
}
