using Operacoes.Domain.Common;

namespace Operacoes.Application.Catalogo;

public static class CatalogoErrors
{

    public static readonly Error HubIndisponivel =
        new(
            "Hub.Indisponivel",
            "Não foi possível validar o instrumento agora; tente novamente.",
            ErrorType.Unavailable);
}
