using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure.Catalogo;

namespace Operacoes.Infrastructure.Tests.Catalogo;

public sealed class HubCatalogoClientTests
{
    private const string BaseUrl = "http://hub.internal/";

    private static HubCatalogoClient CriarCliente(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return new HubCatalogoClient(httpClient, NullLogger<HubCatalogoClient>.Instance);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComMatchExatoNaPrimeiraPagina_DevolveTrue()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            """[{"id":"td:tesouro-selic-2029"},{"id":"td:tesouro-ipca-2035"}]""",
            new Dictionary<string, string> { ["X-Total-Count"] = "2" }));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_SemMatchESemXTotalCount_NaoConsultaSegundaPaginaEDevolveFalse()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK, """[{"id":"td:outro-instrumento"}]"""));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value);
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComMatchSomenteNaSegundaPagina_PercorreAsPaginasEDevolveTrue()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """[{"id":"td:outro-instrumento"}]""",
                new Dictionary<string, string> { ["X-Total-Count"] = "2" }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """[{"id":"td:tesouro-selic-2029"}]"""));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Contains("page=1", handler.RequestedUris[0].Query);
        Assert.Contains("page=2", handler.RequestedUris[1].Query);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_EsgotaTodasAsPaginasSemMatch_DevolveFalse()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """[{"id":"td:a"}]""",
                new Dictionary<string, string> { ["X-Total-Count"] = "2" }))
            .Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """[{"id":"td:b"}]"""));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value);
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComDivergenciaDeCaixaApenas_DevolveFalse()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK, """[{"id":"TD:Tesouro-Selic-2029"}]"""));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task InstrumentoExisteAsync_ComRespostaNaoOk_DevolveUnavailableNuncaNaoExiste(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(status));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
        Assert.Equal("Hub.Indisponivel", resultado.Error.Code);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComCorpoIlegivel_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.BrokenBodyResponse());

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComJsonNulo_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "null"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComExcecaoDeTransporte_DevolveUnavailable()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(_ => throw new HttpRequestException("conexão recusada"));

        var cliente = CriarCliente(handler);

        var resultado = await cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
    }

    [Fact]
    public async Task InstrumentoExisteAsync_ComTokenJaCancelado_Lanca()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(_ => throw new OperationCanceledException());
        var cliente = CriarCliente(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cliente.InstrumentoExisteAsync("td:tesouro-selic-2029", cts.Token));
    }
}
