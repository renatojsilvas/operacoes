namespace Operacoes.Domain.Operacoes;

public static class OperacaoNumericLimits
{
    public const int QuantidadePrecisao = 18;

    public const int QuantidadeEscala = 8;

    public const int ValorFinanceiroPrecisao = 18;

    public const int ValorFinanceiroEscala = 2;

    public static readonly decimal QuantidadeLimiteSuperiorExclusivo =
        Potencia10(QuantidadePrecisao - QuantidadeEscala);

    public static readonly decimal ValorFinanceiroLimiteSuperiorExclusivo =
        Potencia10(ValorFinanceiroPrecisao - ValorFinanceiroEscala);

    private static decimal Potencia10(int expoente)
    {
        var resultado = 1m;
        for (var i = 0; i < expoente; i++)
        {
            resultado *= 10m;
        }

        return resultado;
    }
}
