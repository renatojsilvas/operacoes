using Operacoes.Application.Catalogo;
using Operacoes.Application.Instrumentos;
using Operacoes.Application.Tests.Operacoes;
using Operacoes.Domain.Common;

namespace Operacoes.Application.Tests.Instrumentos;

public sealed class ListarInstrumentosQueryHandlerTests
{
    private static InstrumentoCatalogo Item(string id, bool vencido = false) =>
        new(id, "titulo-publico", id, vencido);

    private static ListarInstrumentosQuery Consulta(
        string? query = "termo",
        string? clienteId = null,
        bool incluirVencidos = false,
        int? limit = null) =>
        new(query, clienteId, incluirVencidos, limit);

    private static ListarInstrumentosQueryHandler CriarHandler(
        FakeHubCatalogoClient hub, FakeOperacaoReadRepository readRepository) =>
        new(hub, readRepository);

    [Fact]
    public async Task Handle_QueryIdExatoDeVencidoSemIncluirVencidos_VemNaPosicaoZero()
    {
        var vencido = Item("td:vencido-2020", vencido: true);
        var hub = new FakeHubCatalogoClient { Catalogo = [vencido] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(
            Consulta(query: vencido.Id, incluirVencidos: false), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.NotEmpty(resultado.Value.Itens);
        Assert.Equal(vencido.Id, resultado.Value.Itens[0].Id);
        Assert.True(resultado.Value.Itens[0].Vencido);
        Assert.Equal(1, resultado.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_QueryPrefixoDoVencidoSemSerExatoSemIncluirVencidos_VencidoNaoAparece()
    {
        var vencido = Item("td:vencido-prefixo-2020", vencido: true);
        var controle = Item("td:controle-nao-vencido");
        var hub = new FakeHubCatalogoClient { Catalogo = [vencido, controle] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(
            Consulta(query: "td:vencido-prefixo", incluirVencidos: false), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.NotEmpty(resultado.Value.Itens);
        Assert.Contains(resultado.Value.Itens, item => item.Id == controle.Id);
        Assert.DoesNotContain(resultado.Value.Itens, item => item.Id == vencido.Id);
    }

    [Fact]
    public async Task Handle_ComIncluirVencidosTrueEQueryExata_VencidoAparece()
    {
        var vencido = Item("td:vencido-incluido-2020", vencido: true);
        var hub = new FakeHubCatalogoClient { Catalogo = [vencido] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(
            Consulta(query: vencido.Id, incluirVencidos: true), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Contains(resultado.Value.Itens, item => item.Id == vencido.Id);
    }

    [Fact]
    public async Task Handle_ComIncluirVencidosTrueEQueryPrefixo_VencidoAparece()
    {
        var vencido = Item("td:vencido-incluido-2020", vencido: true);
        var hub = new FakeHubCatalogoClient { Catalogo = [vencido] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(
            Consulta(query: "td:vencido-incluido", incluirVencidos: true), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Contains(resultado.Value.Itens, item => item.Id == vencido.Id);
    }

    [Fact]
    public async Task Handle_ComTresCriteriosCompetindo_OrdenaExatoPrimeiroDepoisJaNegociadoDepoisId()
    {
        var exato = Item("ordem-competicao");
        var negociadoZ = Item("ordem-competicao-z-negociado");
        var naoNegociadoA = Item("ordem-competicao-a-nao-negociado");
        var naoNegociadoB = Item("ordem-competicao-b-nao-negociado");

        var hub = new FakeHubCatalogoClient { Catalogo = [naoNegociadoB, negociadoZ, exato, naoNegociadoA] };
        var readRepository = new FakeOperacaoReadRepository
        {
            InstrumentosNegociados = Result<IReadOnlyList<string>>.Success([negociadoZ.Id]),
        };
        var handler = CriarHandler(hub, readRepository);

        var resultado = await handler.Handle(
            Consulta(query: "ordem-competicao", clienteId: "cliente-1", limit: 10), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var ids = resultado.Value.Itens.Select(item => item.Id).ToList();
        Assert.Equal([exato.Id, negociadoZ.Id, naoNegociadoA.Id, naoNegociadoB.Id], ids);
    }

    [Fact]
    public async Task Handle_ComClienteId_JaNegociadoPreenchidoParaTodosOsItens()
    {
        var negociado = Item("td:ja-negociado");
        var naoNegociado = Item("td:nao-negociado");
        var hub = new FakeHubCatalogoClient { Catalogo = [negociado, naoNegociado] };
        var readRepository = new FakeOperacaoReadRepository
        {
            InstrumentosNegociados = Result<IReadOnlyList<string>>.Success([negociado.Id]),
        };
        var handler = CriarHandler(hub, readRepository);

        var resultado = await handler.Handle(
            Consulta(query: "td:", clienteId: "cliente-1"), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.NotEmpty(resultado.Value.Itens);
        Assert.All(resultado.Value.Itens, item => Assert.True(item.JaNegociado.HasValue));
        Assert.True(resultado.Value.Itens.Single(item => item.Id == negociado.Id).JaNegociado);
        Assert.False(resultado.Value.Itens.Single(item => item.Id == naoNegociado.Id).JaNegociado);
    }

    [Fact]
    public async Task Handle_SemClienteId_JaNegociadoNuloParaTodosOsItens()
    {
        var item = Item("td:sem-cliente-id");
        var hub = new FakeHubCatalogoClient { Catalogo = [item] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(query: "td:sem-cliente-id"), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.NotEmpty(resultado.Value.Itens);
        Assert.All(resultado.Value.Itens, item => Assert.Null(item.JaNegociado));
    }

    [Fact]
    public async Task Handle_QuandoNenhumItemDoHubCasaExatoComOTermo_TotalCountReflecteTodosEOrdenaPorId()
    {
        var primeiro = Item("td:sem-relacao-b");
        var segundo = Item("td:sem-relacao-a");
        var hub = new FakeHubCatalogoClient { Catalogo = [primeiro, segundo] };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(
            Consulta(query: "termo-sem-nenhum-match-exato"), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.TotalCount);
        Assert.Equal([segundo.Id, primeiro.Id], resultado.Value.Itens.Select(item => item.Id).ToList());
    }

    [Fact]
    public async Task Handle_SemLimit_UsaDefaultDeVinte()
    {
        var itens = Enumerable.Range(1, 25).Select(i => Item($"td:item-{i:D2}")).ToList();
        var hub = new FakeHubCatalogoClient { Catalogo = itens };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(query: "td:item", limit: null), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(25, resultado.Value.TotalCount);
        Assert.Equal(20, resultado.Value.Itens.Count);
    }

    [Fact]
    public async Task Handle_ComLimitAcimaDoMaximo_ClampaEmCinquenta()
    {
        var itens = Enumerable.Range(1, 60).Select(i => Item($"td:clamp-{i:D2}")).ToList();
        var hub = new FakeHubCatalogoClient { Catalogo = itens };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(query: "td:clamp", limit: 999), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(60, resultado.Value.TotalCount);
        Assert.Equal(50, resultado.Value.Itens.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Handle_ComLimitInvalido_DevolveErroDeLimitInvalido(int limit)
    {
        var hub = new FakeHubCatalogoClient();
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(limit: limit), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(RequisicaoErrors.InstrumentosLimitInvalido, resultado.Error);
        Assert.Empty(hub.Chamadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_ComQueryInvalida_DevolveErroDeQueryObrigatoria(string? query)
    {
        var hub = new FakeHubCatalogoClient();
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(query: query), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(RequisicaoErrors.InstrumentosQueryObrigatoria, resultado.Error);
        Assert.Empty(hub.Chamadas);
    }

    [Fact]
    public async Task Handle_QuandoHubFalha_DevolveErroDoHubSemConsultarNegociados()
    {
        var hub = new FakeHubCatalogoClient(Result<bool>.Failure(CatalogoErrors.HubIndisponivel));
        var readRepository = new FakeOperacaoReadRepository();
        var handler = CriarHandler(hub, readRepository);

        var resultado = await handler.Handle(
            Consulta(query: "termo", clienteId: "cliente-1"), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(CatalogoErrors.HubIndisponivel, resultado.Error);
        Assert.Empty(readRepository.ChamadasDeNegociados);
    }

    [Fact]
    public async Task Handle_QuandoReadRepositoryDeNegociadosFalha_DevolveErroDele()
    {
        var erro = DomainErrors.General.Validation("falha ao consultar negociados");
        var hub = new FakeHubCatalogoClient { Catalogo = [Item("td:qualquer")] };
        var readRepository = new FakeOperacaoReadRepository
        {
            InstrumentosNegociados = Result<IReadOnlyList<string>>.Failure(erro),
        };
        var handler = CriarHandler(hub, readRepository);

        var resultado = await handler.Handle(
            Consulta(query: "td:qualquer", clienteId: "cliente-1"), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(erro, resultado.Error);
    }

    [Fact]
    public async Task Handle_ComMaisMatchesQueLimit_TotalCountReflecteTotalAntesDoCorte()
    {
        var itens = Enumerable.Range(1, 5).Select(i => Item($"td:truncamento-{i}")).ToList();
        var hub = new FakeHubCatalogoClient { Catalogo = itens };
        var handler = CriarHandler(hub, new FakeOperacaoReadRepository());

        var resultado = await handler.Handle(Consulta(query: "td:truncamento", limit: 2), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.Itens.Count);
        Assert.Equal(5, resultado.Value.TotalCount);
        Assert.True(resultado.Value.TotalCount > resultado.Value.Itens.Count);
    }
}
