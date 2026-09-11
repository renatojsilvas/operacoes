using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class OperacoesEndpointsTests : IDisposable
{
    private const string PastPastDate = "2020-01-01";

    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;
    private readonly string _connectionString;

    public OperacoesEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
        _connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection não foi definida pela ApiTestFactory.");

        _factory.HubCatalogoClient.Reset();
    }

    public void Dispose()
    {
        _factory.HubCatalogoClient.Reset();
    }

    private static object CorpoValido(
        string clienteId,
        string instrumentoId,
        string tipo = "aporte",
        decimal quantidade = 10m,
        decimal valorFinanceiro = 1000m,
        string dataEvento = PastPastDate,
        string? estornaOperacaoId = null,
        decimal? valorOrigemSaldo = 500m) => new
        {
            clienteId,
            instrumentoId,
            tipo,
            quantidade,
            valorFinanceiro,
            dataEvento,
            estornaOperacaoId,
            valorOrigemSaldo,
        };

    private static HttpRequestMessage BuildRequest(object corpo, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = JsonContent.Create(corpo),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<string?> GetCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string NovoId() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Post_ComOperacaoValida_Retorna201SemLocationComNoStore()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var request = BuildRequest(CorpoValido(clienteId, instrumentoId), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.StartsWith("op-", root.GetProperty("id").GetString());
        Assert.Equal(clienteId, root.GetProperty("clienteId").GetString());
        Assert.Equal(instrumentoId, root.GetProperty("instrumentoId").GetString());
        Assert.Equal("aporte", root.GetProperty("tipo").GetString());
        Assert.Equal(10m, root.GetProperty("quantidade").GetDecimal());
        Assert.Equal(1000m, root.GetProperty("valorFinanceiro").GetDecimal());
    }

    [Fact]
    public async Task Post_SemIdempotencyKey_Retorna400ComCode()
    {
        using var request = BuildRequest(CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029"), idempotencyKey: null);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Requisicao.IdempotencyKeyAusente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComIdempotencyKeyVazia_Retorna400ComCode()
    {
        using var request = BuildRequest(CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029"), idempotencyKey: "");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Requisicao.IdempotencyKeyAusente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComDuploEstornoDaMesmaOperacao_PrimeiroCria201SegundoDevolve409()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var original = BuildRequest(CorpoValido(clienteId, instrumentoId), $"idem-original-{NovoId()}");
        var originalResponse = await _client.SendAsync(original);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        var originalBody = await originalResponse.Content.ReadAsStringAsync();
        var originalId = JsonDocument.Parse(originalBody).RootElement.GetProperty("id").GetString()!;

        var corpoEstorno = CorpoValido(clienteId, instrumentoId, tipo: "estorno", estornaOperacaoId: originalId, valorOrigemSaldo: null);

        using var estorno1 = BuildRequest(corpoEstorno, $"idem-estorno-1-{NovoId()}");
        var estorno1Response = await _client.SendAsync(estorno1);
        Assert.Equal(HttpStatusCode.Created, estorno1Response.StatusCode);

        using var estorno2 = BuildRequest(corpoEstorno, $"idem-estorno-2-{NovoId()}");
        var estorno2Response = await _client.SendAsync(estorno2);

        Assert.Equal(HttpStatusCode.Conflict, estorno2Response.StatusCode);
        Assert.Equal("Operacao.EstornoJaRealizado", await GetCodeAsync(estorno2Response));

        await using var connection = await OpenConnectionAsync();
        var totalEstornos = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE estorna_operacao_id = @id", new { id = originalId });
        Assert.Equal(1, totalEstornos);
    }

    [Fact]
    public async Task Post_ComDezEstornosConcorrentesChavesDiferentesMesmaOperacao_ApenasUmVenceSemNenhumQuinhentos()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var original = BuildRequest(CorpoValido(clienteId, instrumentoId), $"idem-orig-{NovoId()}");
        var originalResponse = await _client.SendAsync(original);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        var originalId = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var corpoEstorno = CorpoValido(clienteId, instrumentoId, tipo: "estorno", estornaOperacaoId: originalId, valorOrigemSaldo: null);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            using var request = BuildRequest(corpoEstorno, $"idem-estorno-{i}-{NovoId()}");
            return (await _client.SendAsync(request)).StatusCode;
        }));

        var criados = respostas.Count(s => s == HttpStatusCode.Created);
        var conflitantes = respostas.Count(s => s == HttpStatusCode.Conflict);
        var outros = respostas.Where(s => s is not HttpStatusCode.Created and not HttpStatusCode.Conflict).ToArray();

        Assert.Equal(1, criados);
        Assert.Equal(9, conflitantes);
        Assert.Empty(outros);

        await using var connection = await OpenConnectionAsync();
        var totalEstornos = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE estorna_operacao_id = @id", new { id = originalId });
        Assert.Equal(1, totalEstornos);
    }

    [Fact]
    public async Task Post_ComMesmaIdempotencyKeyDuasVezes_PrimeiroCria201SegundoDevolve200ReplayComUmaLinhaCadaTabela()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";
        var idempotencyKey = $"idem-{NovoId()}";
        var corpo = CorpoValido(clienteId, instrumentoId);

        using var request1 = BuildRequest(corpo, idempotencyKey);
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        var id1 = JsonDocument.Parse(body1).RootElement.GetProperty("id").GetString();

        using var request2 = BuildRequest(corpo, idempotencyKey);
        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("no-store", response2.Headers.CacheControl?.ToString());
        var body2 = await response2.Content.ReadAsStringAsync();
        var id2 = JsonDocument.Parse(body2).RootElement.GetProperty("id").GetString();

        Assert.Equal(id1, id2);

        await using var connection = await OpenConnectionAsync();
        var totalOperacoes = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = id1 });
        Assert.Equal(1, totalOperacoes);

        var totalOutbox = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM outbox WHERE payload ->> 'tradeId' = @id", new { id = id1 });
        Assert.Equal(1, totalOutbox);
    }

    [Fact]
    public async Task Post_ComMesmaIdempotencyKeyECorpoDiferente_DevolveARespostaDaPrimeiraChamadaSemDetectarDivergencia()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";
        var idempotencyKey = $"idem-{NovoId()}";

        using var request1 = BuildRequest(CorpoValido(clienteId, instrumentoId, quantidade: 10m), idempotencyKey);
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        var id1 = JsonDocument.Parse(body1).RootElement.GetProperty("id").GetString();

        using var request2 = BuildRequest(CorpoValido(clienteId, instrumentoId, quantidade: 999m), idempotencyKey);
        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();
        var root2 = JsonDocument.Parse(body2).RootElement;

        Assert.Equal(id1, root2.GetProperty("id").GetString());
        Assert.Equal(10m, root2.GetProperty("quantidade").GetDecimal());

        await using var connection = await OpenConnectionAsync();
        var totalOperacoes = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = id1 });
        Assert.Equal(1, totalOperacoes);
    }

    [Fact]
    public async Task Post_ComClienteIdComEEmEspacosAoRedorMesmaIdempotencyKey_ColidemNoMesmoIdPorDesenho()
    {
        var clienteIdBase = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";
        var idempotencyKey = $"idem-{NovoId()}";

        using var request1 = BuildRequest(CorpoValido(clienteIdBase, instrumentoId), idempotencyKey);
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var id1 = JsonDocument.Parse(await response1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        using var request2 = BuildRequest(CorpoValido($" {clienteIdBase} ", instrumentoId), idempotencyKey);
        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var id2 = JsonDocument.Parse(await response2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        Assert.Equal(id1, id2);
    }

    [Fact]
    public async Task Post_ComIdempotencyKeyComEEmEspacosAoRedorMesmoClienteId_ColidemNoMesmoIdEUmaLinhaCadaTabela()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";
        var idempotencyKeyBase = $"idem-{NovoId()}";
        var corpo = CorpoValido(clienteId, instrumentoId);

        using var request1 = BuildRequest(corpo, idempotencyKeyBase);
        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var id1 = JsonDocument.Parse(await response1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        using var request2 = BuildRequest(corpo, $" {idempotencyKeyBase} ");
        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var id2 = JsonDocument.Parse(await response2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        Assert.Equal(id1, id2);

        await using var connection = await OpenConnectionAsync();
        var totalOperacoes = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = id1 });
        Assert.Equal(1, totalOperacoes);

        var totalOutbox = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM outbox WHERE payload ->> 'tradeId' = @id", new { id = id1 });
        Assert.Equal(1, totalOutbox);
    }

    [Fact]
    public async Task Post_ComQuantidadeZero_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", quantidade: 0), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.QuantidadeInvalida", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComValorFinanceiroNegativo_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", valorFinanceiro: -1m), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorFinanceiroInvalido", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComQuantidadeComOnzeDigitosInteiros_Retorna422ComCodeNaoQuinhentos()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", quantidade: 12345678901.1m), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.QuantidadeExcedePrecisaoSuportada", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComQuantidadeComNoveCasasDecimais_Retorna422ComCodeNaoQuinhentos()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", quantidade: 1.123456789m), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.QuantidadeExcedePrecisaoSuportada", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComValorFinanceiroComDezesseteDigitosInteiros_Retorna422ComCodeNaoQuinhentos()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", valorFinanceiro: 12345678901234567.1m),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorFinanceiroExcedePrecisaoSuportada", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComValorFinanceiroComTresCasasDecimais_Retorna422ComCodeNaoQuinhentos()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", valorFinanceiro: 1000.123m), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorFinanceiroExcedePrecisaoSuportada", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComDataEventoFutura_Retorna422ComCode()
    {
        var amanha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");

        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", dataEvento: amanha), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.DataEventoFutura", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComTipoInvalido_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: "voo-espacial"), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("TipoOperacao.Invalido", await GetCodeAsync(response));
    }

    [Theory]
    [InlineData("aporte")]
    [InlineData("aplicacao")]
    public async Task Post_ComValorOrigemSaldoAusente_TipoAporteOuAplicacao_Retorna422ComCode(string tipo)
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: tipo, valorOrigemSaldo: null),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorOrigemSaldoIncoerente", await GetCodeAsync(response));
    }

    [Theory]
    [InlineData("resgate")]
    [InlineData("estorno")]
    public async Task Post_ComValorOrigemSaldoPresente_TipoResgateOuEstorno_Retorna422ComCode(string tipo)
    {
        var corpo = tipo == "estorno"
            ? CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: tipo, estornaOperacaoId: $"op-{NovoId()}", valorOrigemSaldo: 500m)
            : CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: tipo, valorOrigemSaldo: 500m);

        using var request = BuildRequest(corpo, $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorOrigemSaldoIncoerente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComValorOrigemSaldoValido_Retorna201ComOCampoNoCorpo()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var request = BuildRequest(
            CorpoValido(clienteId, instrumentoId, valorFinanceiro: 1000m, valorOrigemSaldo: 900m),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(900m, root.GetProperty("valorOrigemSaldo").GetDecimal());
    }

    [Fact]
    public async Task Post_ComValorOrigemSaldoMaiorQueValorFinanceiro_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", valorFinanceiro: 1000m, valorOrigemSaldo: 1000.01m),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorOrigemSaldoExcedeValorFinanceiro", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComValorOrigemSaldoNegativo_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", valorOrigemSaldo: -0.01m),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.ValorOrigemSaldoInvalido", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComEstornaOperacaoIdSemTipoEstorno_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: "aporte", estornaOperacaoId: "op-1"),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.EstornoIncoerente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComEstornaOperacaoIdVazia_TipoEstorno_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: "estorno", estornaOperacaoId: "   ", valorOrigemSaldo: null),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.EstornoReferenciaVazia", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComEstornoReferenciandoOperacaoInexistente_Retorna422ComCode()
    {
        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029", tipo: "estorno", estornaOperacaoId: $"op-{NovoId()}", valorOrigemSaldo: null),
            $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.EstornoReferenciaInvalida", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComEstornoReferenciandoOperacaoDeOutroCliente_Retorna422ComCode()
    {
        var clienteA = $"cliente-{NovoId()}";
        var clienteB = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var original = BuildRequest(CorpoValido(clienteA, instrumentoId), $"idem-{NovoId()}");
        var originalResponse = await _client.SendAsync(original);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        var originalId = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        using var estorno = BuildRequest(
            CorpoValido(clienteB, instrumentoId, tipo: "estorno", estornaOperacaoId: originalId, valorOrigemSaldo: null), $"idem-{NovoId()}");
        var estornoResponse = await _client.SendAsync(estorno);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, estornoResponse.StatusCode);
        Assert.Equal("Operacao.EstornoReferenciaInvalida", await GetCodeAsync(estornoResponse));
    }

    [Fact]
    public async Task Post_QuandoHubDizQueInstrumentoNaoExiste_Retorna422ComCode()
    {
        _factory.HubCatalogoClient.Resposta = Result<bool>.Success(false);

        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:instrumento-desconhecido"), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.InstrumentoInexistente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_QuandoHubDevolveCatalogoNaoVazioSemMatchExato_Retorna422ComCode()
    {
        _factory.HubCatalogoClient.Catalogo =
        [
            new InstrumentoCatalogo("td:x-2030", "titulo-publico", "Tesouro X 2030", Vencido: false),
        ];

        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:x"), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.InstrumentoInexistente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_QuandoHubDevolveItemComIdNulo_NaoDevolve500()
    {
        const string instrumentoId = "instrumento-com-id-nulo-no-hub";
        _factory.HubCatalogoClient.Catalogo =
        [
            new InstrumentoCatalogo(null!, "titulo-publico", instrumentoId, Vencido: false),
        ];

        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", instrumentoId), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.InstrumentoInexistente", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_QuandoHubIndisponivel_Retorna503NuncaComo422Ou500()
    {
        _factory.HubCatalogoClient.Resposta = Result<bool>.Failure(CatalogoErrors.HubIndisponivel);

        using var request = BuildRequest(
            CorpoValido($"cliente-{NovoId()}", "td:tesouro-selic-2029"), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Hub.Indisponivel", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComCorpoIlegivel_Retorna400ComCodePadraoPeloExceptionHandler()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = new StringContent("{ isto não é json válido", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"idem-{NovoId()}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(System.Net.Mime.MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Requisicao.CorpoInvalido", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Post_ComCampoObrigatorioAusente_Retorna400()
    {
        var corpoIncompleto = new
        {

            instrumentoId = "td:tesouro-selic-2029",
            tipo = "aporte",
            quantidade = 10m,
            valorFinanceiro = 1000m,
            dataEvento = PastPastDate,
        };

        using var request = BuildRequest(corpoIncompleto, $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_QuandoRejeitado_NaoGravaNadaNaTabelaOperacoes()
    {
        var clienteId = $"cliente-{NovoId()}";

        using var request = BuildRequest(
            CorpoValido(clienteId, "td:tesouro-selic-2029", quantidade: -5m), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var connection = await OpenConnectionAsync();
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE cliente_id = @clienteId", new { clienteId });
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task Post_ComSucesso_GravaOperacaoEOutboxNaMesmaOperacao()
    {
        var clienteId = $"cliente-{NovoId()}";
        var instrumentoId = "td:tesouro-selic-2029";

        using var request = BuildRequest(CorpoValido(clienteId, instrumentoId), $"idem-{NovoId()}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        await using var connection = await OpenConnectionAsync();

        var totalOperacoes = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id });
        Assert.Equal(1, totalOperacoes);

        var outboxRow = await connection.QuerySingleAsync<(
            string Tipo, string RoutingKey, string ClienteId, string InstrumentoId, string Operacao,
            string Quantidade, string ValorFinanceiro, string? EstornaTradeId)>(
            """
            SELECT
                tipo AS "Tipo",
                routing_key AS "RoutingKey",
                payload ->> 'clienteId' AS "ClienteId",
                payload ->> 'instrumentoId' AS "InstrumentoId",
                payload ->> 'operacao' AS "Operacao",
                payload ->> 'quantidade' AS "Quantidade",
                payload ->> 'valorFinanceiro' AS "ValorFinanceiro",
                payload ->> 'estornaTradeId' AS "EstornaTradeId"
            FROM outbox WHERE payload ->> 'tradeId' = @id
            """,
            new { id });

        Assert.Equal("TradeRegistered", outboxRow.Tipo);
        Assert.Equal("trades.registered", outboxRow.RoutingKey);
        Assert.Equal(clienteId, outboxRow.ClienteId);
        Assert.Equal(instrumentoId, outboxRow.InstrumentoId);
        Assert.Equal("aporte", outboxRow.Operacao);
        Assert.Equal("10.00000000", outboxRow.Quantidade);
        Assert.Equal("1000.00", outboxRow.ValorFinanceiro);

        Assert.Null(outboxRow.EstornaTradeId);
    }
}
