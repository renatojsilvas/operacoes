using Operacoes.Domain.Common;

namespace Operacoes.Application.Catalogo;

public static class CatalogoErrors
{

    public static readonly Error HubIndisponivel =
        new(
            "Hub.Indisponivel",
            "Não foi possível validar o instrumento agora; tente novamente.",
            ErrorType.Unavailable);

    public static readonly Error HubColetaIncompleta =
        new(
            "Hub.ColetaIncompleta",
            "O catálogo do Hub não coube na coleta; resultado seria parcial e não foi devolvido.",
            ErrorType.Unavailable);
}
