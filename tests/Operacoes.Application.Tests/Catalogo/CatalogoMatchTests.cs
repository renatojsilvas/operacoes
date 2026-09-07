using Operacoes.Application.Catalogo;

namespace Operacoes.Application.Tests.Catalogo;

public sealed class CatalogoMatchTests
{
    private static InstrumentoCatalogo Item(string id) => new(id, "titulo-publico", id, Vencido: false);

    [Fact]
    public void ContemExato_ComIdPresente_DevolveTrue()
    {
        var itens = new[] { Item("td:tesouro-selic-2029"), Item("td:tesouro-ipca-2035") };

        Assert.True(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComIdAusente_DevolveFalse()
    {
        var itens = new[] { Item("td:tesouro-ipca-2035") };

        Assert.False(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComListaVazia_DevolveFalse()
    {
        Assert.False(CatalogoMatch.ContemExato([], "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComDivergenciaDeCaixaApenas_DevolveFalse()
    {
        var itens = new[] { Item("TD:Tesouro-Selic-2029") };

        Assert.False(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComEspacosEmVoltaDoIdBuscado_IgnoraOsEspacos()
    {
        var itens = new[] { Item("td:tesouro-selic-2029") };

        Assert.True(CatalogoMatch.ContemExato(itens, "  td:tesouro-selic-2029  "));
    }

    [Fact]
    public void ContemExato_ComEspacosEmVoltaDoIdDoItem_IgnoraOsEspacos()
    {
        var itens = new[] { Item("  td:tesouro-selic-2029  ") };

        Assert.True(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void EhExato_ComItemDeIdNulo_NaoLancaEDevolveFalse()
    {
        var item = new InstrumentoCatalogo(null!, "titulo-publico", "Sem Id", Vencido: false);

        Assert.False(CatalogoMatch.EhExato(item, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComItemDeIdNuloEntreOutros_NaoLancaEIgnoraOItemSemId()
    {
        var itens = new[]
        {
            new InstrumentoCatalogo(null!, "titulo-publico", "Sem Id", Vencido: false),
            Item("td:tesouro-selic-2029"),
        };

        Assert.True(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComApenasItemDeIdNulo_NaoLancaEDevolveFalse()
    {
        var itens = new[] { new InstrumentoCatalogo(null!, "titulo-publico", "Sem Id", Vencido: false) };

        Assert.False(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void EhExato_ComNomeExibicaoIgualAoTermoBuscadoMasIdDiferente_DevolveFalse()
    {
        var item = new InstrumentoCatalogo("td:outro-instrumento", "titulo-publico", "td:tesouro-selic-2029", Vencido: false);

        Assert.False(CatalogoMatch.EhExato(item, "td:tesouro-selic-2029"));
    }

    [Fact]
    public void ContemExato_ComItemCujoNomeExibicaoCasaComOTermoMasCujoIdDiverge_DevolveFalse()
    {
        var itens = new[]
        {
            new InstrumentoCatalogo("td:outro-instrumento", "titulo-publico", "td:tesouro-selic-2029", Vencido: false),
        };

        Assert.False(CatalogoMatch.ContemExato(itens, "td:tesouro-selic-2029"));
    }
}
