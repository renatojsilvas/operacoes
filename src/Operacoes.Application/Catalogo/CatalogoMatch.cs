namespace Operacoes.Application.Catalogo;

public static class CatalogoMatch
{
    public static bool EhExato(InstrumentoCatalogo item, string instrumentoId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var alvo = (instrumentoId ?? string.Empty).Trim();

        return item.Id is not null && string.Equals(item.Id.Trim(), alvo, StringComparison.Ordinal);
    }

    public static bool ContemExato(IReadOnlyList<InstrumentoCatalogo> itens, string instrumentoId)
    {
        ArgumentNullException.ThrowIfNull(itens);

        foreach (var item in itens)
        {
            if (EhExato(item, instrumentoId))
            {
                return true;
            }
        }

        return false;
    }
}
