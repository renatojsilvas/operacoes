using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure.Catalogo;
using Operacoes.Infrastructure.Tests.Common;

namespace Operacoes.Infrastructure.Tests.Catalogo;

public sealed class HubCatalogoClientTests
{
    private const string BaseUrl = "http://hub.internal/";

    private static HubCatalogoClient CriarCliente(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return new HubCatalogoClient(httpClient, NullLogger<HubCatalogoClient>.Instance);
    }

    private static HubCatalogoClient CriarCliente(FakeHttpMessageHandler handler, FakeLogger<HubCatalogoClient> logger)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return new HubCatalogoClient(httpClient, logger);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComUmaPagina_DevolveOConjuntoCru()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """[{"id":"td:tesouro-selic-2029","classe":"titulo-publico","nomeExibicao":"Tesouro Selic 2029","vencido":false},"""
            + """{"id":"td:tesouro-ipca-2035","classe":"titulo-publico","nomeExibicao":"Tesouro IPCA+ 2035","vencido":false}]""",
            new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.Count);
        Assert.Contains(resultado.Value, i => i.Id == "td:tesouro-selic-2029");
        Assert.Contains(resultado.Value, i => i.Id == "td:tesouro-ipca-2035");
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_NaoAplicaNenhumFiltroDeVencidoNemDePrioridade()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """[{"id":"td:tesouro-selic-2020","classe":"titulo-publico","nomeExibicao":"Tesouro Selic 2020","vencido":true}]""",
            new Dictionary<string, string> { ["X-Total-Count"] = "1" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value);
        Assert.Equal("td:tesouro-selic-2020", item.Id);
        Assert.True(item.Vencido);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_VencidoERepassadoDoHubNuncaRecalculadoAPartirDeAtivoAte()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """
            [{"id":"td:tesouro-selic-2020","classe":"titulo-publico","nomeExibicao":"Tesouro Selic 2020","ativoAte":"2020-01-01","vencido":false}]
            """));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value);
        Assert.False(item.Vencido);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountAtravessaAsPaginasEDevolveOConjuntoCompleto()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]",
                new Dictionary<string, string> { ["X-Total-Count"] = "501" }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, $"[{ItemJson(500)}]"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(501, resultado.Value.Count);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Contains("page=1", handler.RequestedUris[0].Query);
        Assert.Contains("page=2", handler.RequestedUris[1].Query);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountMultiploExatoDoTamanhoDaPagina_EncerraAoAtingirOTotalSemPedirPaginaExtra()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]",
            new Dictionary<string, string> { ["X-Total-Count"] = "500" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(500, resultado.Value.Count);
        Assert.Single(handler.RequestedUris);
        Assert.Contains("page=1", handler.RequestedUris[0].Query);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountNaoNumerico_IgnoraOHeaderEContinuaPaginandoPelaPaginaParcial()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            $"[{ItemJson(0)}]",
            new Dictionary<string, string> { ["X-Total-Count"] = "quantidade-desconhecida" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Single(resultado.Value);
        Assert.Single(handler.RequestedUris);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("quantidade-desconhecida")]
    public async Task BuscarPorTermoAsync_ComXTotalCountZeroNegativoOuNaoNumericoAoFimDaPrimeiraPaginaCheia_IgnoraOHeaderEBuscaAPaginaSeguinte(
        string totalCountHeader)
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]",
                new Dictionary<string, string> { ["X-Total-Count"] = totalCountHeader }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, $"[{ItemJson(500)}]"));

        var logger = new FakeLogger<HubCatalogoClient>();
        var cliente = CriarCliente(handler, logger);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(501, resultado.Value.Count);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Empty(logger.Entries.Where(entry => entry.Level >= LogLevel.Warning));
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountMenorQueOColetadoAoFimDaPrimeiraPaginaCheia_DescartaOHeaderComWarningEBuscaAPaginaSeguinte()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]",
                new Dictionary<string, string> { ["X-Total-Count"] = "100" }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, $"[{ItemJson(500)}]"));

        var logger = new FakeLogger<HubCatalogoClient>();
        var cliente = CriarCliente(handler, logger);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(501, resultado.Value.Count);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("100", StringComparison.Ordinal)
                && entry.Message.Contains("500", StringComparison.Ordinal)
                && entry.Message.Contains("td", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuscarPorTermoAsync_SemXTotalCountEComPaginaCheia_ContinuaPaginandoAteEncontrarPaginaMenorQueOTamanho()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]"))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, $"[{ItemJson(500)}]"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(501, resultado.Value.Count);
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_SemXTotalCountEComPaginaParcial_EncerraNaPrimeiraPagina()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK, $"[{ItemJson(0)}]"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Single(resultado.Value);
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComPaginaVaziaAntesDoTotalAnunciado_DevolveFalha()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]",
                new Dictionary<string, string> { ["X-Total-Count"] = "1000" }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "[]"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(CatalogoErrors.HubColetaIncompleta, resultado.Error);
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountMaiorQueOColetadoAoFimDoLaco_DevolveFalha()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            $"[{ItemJson(0)}]",
            new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(CatalogoErrors.HubColetaIncompleta, resultado.Error);
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComHubSempreDevolvendoPaginaCheia_AtingeTetoDevolveFalhaSemLacoInfinito()
    {
        var handler = new FakeHttpMessageHandler();
        for (var i = 0; i < 200; i++)
        {
            handler.Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                string.Join(",", Enumerable.Range(0, 500).Select(ItemJson)).Insert(0, "[") + "]"));
        }

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(CatalogoErrors.HubColetaIncompleta, resultado.Error);
        Assert.True(handler.RequestedUris.Count <= 100, "Cliente deveria ter um teto de páginas e não seguir indefinidamente.");
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComItemDeIdNuloVazioOuComEspacos_DescartaOsItensELogaWarning()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """
            [{"id":"td:tesouro-selic-2029","classe":"titulo-publico","nomeExibicao":"Tesouro Selic 2029","vencido":false},
            {"id":null,"classe":"titulo-publico","nomeExibicao":"Sem Id","vencido":false},
            {"id":"","classe":"titulo-publico","nomeExibicao":"Id Vazio","vencido":false},
            {"id":"   ","classe":"titulo-publico","nomeExibicao":"Id So Espacos","vencido":false}]
            """));

        var logger = new FakeLogger<HubCatalogoClient>();
        var cliente = CriarCliente(handler, logger);

        var resultado = await cliente.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value);
        Assert.Equal("td:tesouro-selic-2029", item.Id);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComXTotalCountEItemDeIdNuloNaMesmaPagina_DevolveSucessoComItemDescartado()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """
            [{"id":"td:tesouro-selic-2029","classe":"titulo-publico","nomeExibicao":"Tesouro Selic 2029","vencido":false},
            {"id":null,"classe":"titulo-publico","nomeExibicao":"Sem Id","vencido":false}]
            """,
            new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("tesouro", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var item = Assert.Single(resultado.Value);
        Assert.Equal("td:tesouro-selic-2029", item.Id);
        Assert.Single(handler.RequestedUris);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task BuscarPorTermoAsync_ComRespostaNaoOk_DevolveUnavailable(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(status));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
        Assert.Equal("Hub.Indisponivel", resultado.Error.Code);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComCorpoIlegivel_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.BrokenBodyResponse());

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComJsonNulo_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "null"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComExcecaoDeTransporte_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(_ => throw new HttpRequestException("conexão recusada"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.BuscarPorTermoAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task BuscarPorTermoAsync_ComTokenJaCancelado_Lanca()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(_ => throw new OperationCanceledException());
        var cliente = CriarCliente(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cliente.BuscarPorTermoAsync("td:tesouro-selic-2029", cts.Token));
    }

    private static string ItemJson(int indice) =>
        $$"""{"id":"td:instrumento-{{indice}}","classe":"titulo-publico","nomeExibicao":"Instrumento {{indice}}","vencido":false}""";
}
