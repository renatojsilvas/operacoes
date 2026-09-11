using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;
using Operacoes.Infrastructure.Caching;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class InstrumentosEndpointsTests : IDisposable
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    private static readonly InstrumentoCatalogo CasoBase = Item("td:ABC", "Tesouro ABC", vencido: false);
    private static readonly InstrumentoCatalogo CasoMinusculo = Item("td:abc", "Tesouro abc minusculo", vencido: false);
    private static readonly InstrumentoCatalogo CasoComEspaco =
        Item("  td:com-espaco  ", "Tesouro Com Espaco", vencido: false);
    private static readonly InstrumentoCatalogo CasoPrefixo = Item("td:x", "Tesouro X", vencido: false);
    private static readonly InstrumentoCatalogo CasoPrefixado = Item("td:x-2030", "Tesouro X 2030", vencido: false);
    private static readonly InstrumentoCatalogo CasoVencidoExato =
        Item("td:vencido-2020", "Tesouro Vencido 2020", vencido: true);
    private static readonly InstrumentoCatalogo CasoVencidoCaseDiferente =
        Item("TD:VENCIDO-2020", "Tesouro Vencido Case Diferente", vencido: true);
    private static readonly InstrumentoCatalogo CasoNomeCasaIdNao =
        Item("xyz:sem-relacao-001", "Papel Especial Raro", vencido: false);

    private static readonly IReadOnlyList<InstrumentoCatalogo> CatalogoArmadilhas =
    [
        CasoBase,
        CasoMinusculo,
        CasoComEspaco,
        CasoPrefixo,
        CasoPrefixado,
        CasoVencidoExato,
        CasoVencidoCaseDiferente,
        CasoNomeCasaIdNao,
    ];

    public InstrumentosEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
        _factory.HubCatalogoClient.Reset();
    }

    public void Dispose()
    {
        _factory.HubCatalogoClient.Reset();
    }

    private static InstrumentoCatalogo Item(string id, string nomeExibicao, bool vencido) =>
        new(id, "titulo-publico", nomeExibicao, vencido);

    private static string NovoId() => Guid.NewGuid().ToString("N");

    private static async Task<List<JsonElement>> ParseItensAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static async Task<string?> GetCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task RegistrarOperacaoAsync(string clienteId, string instrumentoId)
    {
        var corpo = new
        {
            clienteId,
            instrumentoId,
            tipo = "aporte",
            quantidade = 1m,
            valorFinanceiro = 10m,
            dataEvento = "2020-01-01",
            estornaOperacaoId = (string?)null,
            valorOrigemSaldo = 5m,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = JsonContent.Create(corpo),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"idem-{NovoId()}");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task AssertPostNaoRejeitaComoInexistenteAsync(string instrumentoId)
    {
        var corpo = new
        {
            clienteId = $"cliente-consistencia-{NovoId()}",
            instrumentoId,
            tipo = "aporte",
            quantidade = 1m,
            valorFinanceiro = 10m,
            dataEvento = "2020-01-01",
            estornaOperacaoId = (string?)null,
            valorOrigemSaldo = 5m,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = JsonContent.Create(corpo),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"idem-consistencia-{NovoId()}");

        var response = await _client.SendAsync(request);
        var code = await GetCodeAsync(response);

        Assert.NotEqual("Operacao.InstrumentoInexistente", code);
    }

    private async Task AssertPostRejeitaComoInexistenteAsync(string instrumentoId)
    {
        var corpo = new
        {
            clienteId = $"cliente-consistencia-{NovoId()}",
            instrumentoId,
            tipo = "aporte",
            quantidade = 1m,
            valorFinanceiro = 10m,
            dataEvento = "2020-01-01",
            estornaOperacaoId = (string?)null,
            valorOrigemSaldo = 5m,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes")
        {
            Content = JsonContent.Create(corpo),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"idem-consistencia-{NovoId()}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Operacao.InstrumentoInexistente", await GetCodeAsync(response));
    }

    private async Task<List<JsonElement>> ObterItensAsync(string termo, bool incluirVencidos, int limit = 50)
    {
        var uri =
            $"/v1/operacoes/instrumentos?query={Uri.EscapeDataString(termo)}" +
            $"&incluirVencidos={(incluirVencidos ? "true" : "false")}&limit={limit}";
        var response = await _client.GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ParseItensAsync(response);
    }

    [Fact]
    public async Task Get_ConsistenciaCruzadaComPost_ParaTodoItemDaListaOPostNuncaFalhaComoInexistente()
    {
        _factory.HubCatalogoClient.Catalogo = CatalogoArmadilhas;

        var termosGerais = new[] { "td:", "abc", "x", "espaco", "especial" };
        var idsExercitados = new HashSet<string>(StringComparer.Ordinal);

        foreach (var termo in termosGerais)
        {
            var itens = await ObterItensAsync(termo, incluirVencidos: true);

            Assert.NotEmpty(itens);

            foreach (var item in itens)
            {
                var instrumentoId = item.GetProperty("id").GetString()!;
                idsExercitados.Add(instrumentoId);

                await AssertPostNaoRejeitaComoInexistenteAsync(instrumentoId);
            }
        }

        Assert.Contains(CasoBase.Id, idsExercitados);
        Assert.Contains(CasoMinusculo.Id, idsExercitados);
        Assert.Contains(CasoComEspaco.Id, idsExercitados);
        Assert.Contains(CasoPrefixo.Id, idsExercitados);
        Assert.Contains(CasoPrefixado.Id, idsExercitados);
        Assert.Contains(CasoNomeCasaIdNao.Id, idsExercitados);

        var itensVencidoExato = await ObterItensAsync(CasoVencidoExato.Id, incluirVencidos: false);
        var idsVencidoExato = itensVencidoExato.Select(item => item.GetProperty("id").GetString()).ToList();

        Assert.NotEmpty(itensVencidoExato);
        Assert.Equal(CasoVencidoExato.Id, itensVencidoExato[0].GetProperty("id").GetString());
        Assert.True(itensVencidoExato[0].GetProperty("vencido").GetBoolean());
        Assert.DoesNotContain(CasoVencidoCaseDiferente.Id, idsVencidoExato);

        await AssertPostNaoRejeitaComoInexistenteAsync(CasoVencidoExato.Id);
    }

    [Fact]
    public async Task Get_ConsistenciaCruzadaComPost_IdComArmadilhaNaoOfertadoPeloHubERecusadoComoInexistente()
    {
        _factory.HubCatalogoClient.Catalogo = CatalogoArmadilhas;

        var idsNaoOfertadosComArmadilha = new[] { "td:x-203", "TD:ABC", "especial" };

        foreach (var instrumentoId in idsNaoOfertadosComArmadilha)
        {
            var itens = await ObterItensAsync(instrumentoId, incluirVencidos: true);

            Assert.NotEmpty(itens);
            Assert.DoesNotContain(itens, item => item.GetProperty("id").GetString() == instrumentoId);

            await AssertPostRejeitaComoInexistenteAsync(instrumentoId);
        }
    }

    [Fact]
    public async Task Get_ComQueryValida_Retorna200ComEtagECacheControlPrivadoSemPublic()
    {
        _factory.HubCatalogoClient.Catalogo = [Item("td:cache-control-1", "Tesouro Cache", vencido: false)];

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=cache-control");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.False(response.Headers.CacheControl!.Public);
        Assert.True(response.Headers.CacheControl.Private);
    }

    [Fact]
    public async Task Get_ComIfNoneMatchIgual_Retorna304SemCorpo()
    {
        _factory.HubCatalogoClient.Catalogo = [Item("td:etag-1", "Tesouro Etag", vencido: false)];

        var primeira = await _client.GetAsync("/v1/operacoes/instrumentos?query=etag-1");
        var etag = primeira.Headers.ETag!.Tag;

        using var condicional = new HttpRequestMessage(HttpMethod.Get, "/v1/operacoes/instrumentos?query=etag-1");
        condicional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var segunda = await _client.SendAsync(condicional);

        Assert.Equal(HttpStatusCode.NotModified, segunda.StatusCode);
        var corpo = await segunda.Content.ReadAsStringAsync();
        Assert.Empty(corpo);
    }

    [Fact]
    public async Task Head_DevolveMesmoEtagDoGetSemCorpo()
    {
        _factory.HubCatalogoClient.Catalogo = [Item("td:head-1", "Tesouro Head", vencido: false)];

        var get = await _client.GetAsync("/v1/operacoes/instrumentos?query=head-1");

        using var head = new HttpRequestMessage(HttpMethod.Head, "/v1/operacoes/instrumentos?query=head-1");
        var respostaHead = await _client.SendAsync(head);

        Assert.Equal(HttpStatusCode.OK, respostaHead.StatusCode);
        var corpo = await respostaHead.Content.ReadAsStringAsync();
        Assert.Empty(corpo);
        Assert.Equal(get.Headers.ETag!.Tag, respostaHead.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Options_Retorna204ComAllow()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/operacoes/instrumentos");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var allow = string.Join(",", response.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Post_NaRotaDeInstrumentos_Retorna405ComAllow()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operacoes/instrumentos");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var allow = string.Join(",", response.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Get_ComMaisMatchesQueLimit_XTotalCountReflecteTotalAntesDoCorte()
    {
        _factory.HubCatalogoClient.Catalogo = Enumerable.Range(1, 5)
            .Select(i => Item($"td:truncamento-{i}", $"Tesouro Truncamento {i}", vencido: false))
            .ToList();

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=truncamento&limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("5", response.Headers.GetValues("X-Total-Count").Single());

        var itens = await ParseItensAsync(response);
        Assert.Equal(2, itens.Count);
        Assert.True(int.Parse(response.Headers.GetValues("X-Total-Count").Single()) > itens.Count);
    }

    [Fact]
    public async Task Get_NuncaExpoeHeaderLink()
    {
        _factory.HubCatalogoClient.Catalogo = [Item("td:sem-link", "Tesouro Sem Link", vencido: false)];

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=sem-link");

        Assert.False(response.Headers.Contains("Link"));
    }

    [Fact]
    public async Task Get_ComLimitAcimaDoMaximo_ClampaEm50()
    {
        _factory.HubCatalogoClient.Catalogo = Enumerable.Range(1, 60)
            .Select(i => Item($"td:clamp-{i:D2}", $"Tesouro Clamp {i}", vencido: false))
            .ToList();

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=clamp&limit=999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var itens = await ParseItensAsync(response);
        Assert.Equal(50, itens.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Get_ComLimitInvalido_Retorna400ComCode(int limit)
    {
        var response = await _client.GetAsync($"/v1/operacoes/instrumentos?query=qualquer&limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Instrumentos.LimitInvalido", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Get_SemQuery_Retorna400ComCode()
    {
        var response = await _client.GetAsync("/v1/operacoes/instrumentos");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Instrumentos.QueryObrigatoria", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Get_ComQueryVazia_Retorna400ComCode()
    {
        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Instrumentos.QueryObrigatoria", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Get_ComQuerySoEspacos_Retorna400ComCode()
    {
        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=%20%20%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Instrumentos.QueryObrigatoria", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Get_QuandoHubIndisponivel_Retorna503SemEtagSemCacheControl()
    {
        _factory.HubCatalogoClient.Resposta = Result<bool>.Failure(CatalogoErrors.HubIndisponivel);

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=qualquer");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Hub.Indisponivel", await GetCodeAsync(response));
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task Get_QuandoHubColetaIncompleta_Retorna503ComCodeDistintoDoHubIndisponivel()
    {
        _factory.HubCatalogoClient.Resposta = Result<bool>.Failure(CatalogoErrors.HubColetaIncompleta);

        var response = await _client.GetAsync("/v1/operacoes/instrumentos?query=qualquer");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Hub.ColetaIncompleta", await GetCodeAsync(response));
    }

    [Fact]
    public async Task Get_QueryIdExatoDeVencidoSemIncluirVencidos_VemNaPosicaoZero()
    {
        var vencido = Item("td:vencido-exato-2020", "Tesouro Vencido Exato", vencido: true);
        var vigenteJaNegociado = Item("td:vencido-exato-2020-negociado", "Tesouro Vencido Exato Negociado", vencido: false);
        var vigenteNaoNegociadoA = Item("td:vencido-exato-2020-a", "Tesouro Vencido Exato A", vencido: false);
        var vigenteNaoNegociadoB = Item("td:vencido-exato-2020-b", "Tesouro Vencido Exato B", vencido: false);

        _factory.HubCatalogoClient.Catalogo =
            [vigenteNaoNegociadoB, vigenteJaNegociado, vencido, vigenteNaoNegociadoA];

        var clienteId = $"cliente-vencido-exato-{NovoId()}";
        await RegistrarOperacaoAsync(clienteId, vigenteJaNegociado.Id);

        var response = await _client.GetAsync(
            $"/v1/operacoes/instrumentos?query=td:vencido-exato-2020&clienteId={clienteId}&incluirVencidos=false&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var itens = await ParseItensAsync(response);

        var ids = itens.Select(item => item.GetProperty("id").GetString()).ToList();
        Assert.Equal(
            [vencido.Id, vigenteJaNegociado.Id, vigenteNaoNegociadoA.Id, vigenteNaoNegociadoB.Id], ids);
        Assert.Equal(vencido.Id, itens[0].GetProperty("id").GetString());
        Assert.True(itens[0].GetProperty("vencido").GetBoolean());
    }

    [Fact]
    public async Task Get_QueryPrefixoDoVencidoSemSerExatoSemIncluirVencidos_VencidoNaoAparece()
    {
        var vencido = Item("td:vencido-prefixo-2020", "Tesouro Vencido Prefixo", vencido: true);
        _factory.HubCatalogoClient.Catalogo = [vencido];

        var itens = await ObterItensAsync("td:vencido-prefixo", incluirVencidos: false);

        Assert.DoesNotContain(itens, item => item.GetProperty("id").GetString() == vencido.Id);
    }

    [Fact]
    public async Task Get_ComIncluirVencidosTrue_VencidoApareceExatoOuNao()
    {
        var vencido = Item("td:vencido-incluido-2020", "Tesouro Vencido Incluido", vencido: true);
        _factory.HubCatalogoClient.Catalogo = [vencido];

        var itensExato = await ObterItensAsync("td:vencido-incluido-2020", incluirVencidos: true);
        Assert.Contains(itensExato, item => item.GetProperty("id").GetString() == vencido.Id);

        var itensPrefixo = await ObterItensAsync("td:vencido-incluido", incluirVencidos: true);
        Assert.Contains(itensPrefixo, item => item.GetProperty("id").GetString() == vencido.Id);
    }

    [Fact]
    public async Task Get_SemClienteId_PropriedadeJaNegociadoNaoExisteNoJson()
    {
        _factory.HubCatalogoClient.Catalogo = [Item("td:sem-cliente-id", "Tesouro Sem ClienteId", vencido: false)];

        var itens = await ObterItensAsync("sem-cliente-id", incluirVencidos: false);

        Assert.NotEmpty(itens);
        Assert.False(itens[0].TryGetProperty("jaNegociado", out _));
    }

    [Fact]
    public async Task Get_ComDoisClienteIdsDistintos_ConjuntosDistintosEUmaUnicaChamadaAoHubPeloCache()
    {
        const string instrumentoA = "td:ja-negociado-a";
        const string instrumentoB = "td:ja-negociado-b";

        var clienteX = $"cliente-jn-{NovoId()}";
        var clienteY = $"cliente-jn-{NovoId()}";

        await RegistrarOperacaoAsync(clienteX, instrumentoA);
        await RegistrarOperacaoAsync(clienteY, instrumentoB);

        var innerFake = new FakeHubCatalogoClient
        {
            Catalogo =
            [
                Item(instrumentoA, "Ja Negociado A", vencido: false),
                Item(instrumentoB, "Ja Negociado B", vencido: false),
            ],
        };

        await using var factoryComCache = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHubCatalogoClient>();
                services.AddSingleton<IHubCatalogoClient>(sp => new CachedHubCatalogoClient(
                    innerFake, sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
            }));

        using var client = factoryComCache.CreateClient();
        client.DefaultRequestHeaders.Add(ApiTestFactory.ApiKeyHeader, ApiTestFactory.ValidApiKey);

        var respostaX = await client.GetAsync(
            $"/v1/operacoes/instrumentos?query=ja-negociado&clienteId={clienteX}&incluirVencidos=true&limit=10");
        var itensX = await ParseItensAsync(respostaX);

        var respostaY = await client.GetAsync(
            $"/v1/operacoes/instrumentos?query=ja-negociado&clienteId={clienteY}&incluirVencidos=true&limit=10");
        var itensY = await ParseItensAsync(respostaY);

        Assert.Equal(HttpStatusCode.OK, respostaX.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respostaY.StatusCode);

        Assert.True(itensX.First(item => item.GetProperty("id").GetString() == instrumentoA)
            .GetProperty("jaNegociado").GetBoolean());
        Assert.False(itensX.First(item => item.GetProperty("id").GetString() == instrumentoB)
            .GetProperty("jaNegociado").GetBoolean());
        Assert.Equal(instrumentoA, itensX[0].GetProperty("id").GetString());

        Assert.True(itensY.First(item => item.GetProperty("id").GetString() == instrumentoB)
            .GetProperty("jaNegociado").GetBoolean());
        Assert.False(itensY.First(item => item.GetProperty("id").GetString() == instrumentoA)
            .GetProperty("jaNegociado").GetBoolean());
        Assert.Equal(instrumentoB, itensY[0].GetProperty("id").GetString());

        Assert.Single(innerFake.Chamadas);
    }

    [Fact]
    public async Task Get_ComTresCriteriosCompetindo_OrdenaExatoPrimeiroDepoisJaNegociadoDepoisId()
    {
        var exato = Item("ordem-competicao", "Ordem Exata", vencido: false);
        var negociadoZ = Item("ordem-competicao-z-negociado", "Ordem Z Negociado", vencido: false);
        var naoNegociadoA = Item("ordem-competicao-a-nao-negociado", "Ordem A Nao Negociado", vencido: false);
        var naoNegociadoB = Item("ordem-competicao-b-nao-negociado", "Ordem B Nao Negociado", vencido: false);

        _factory.HubCatalogoClient.Catalogo = [naoNegociadoB, negociadoZ, exato, naoNegociadoA];

        var clienteId = $"cliente-ordem-{NovoId()}";
        await RegistrarOperacaoAsync(clienteId, negociadoZ.Id);

        var response = await _client.GetAsync(
            $"/v1/operacoes/instrumentos?query=ordem-competicao&clienteId={clienteId}&limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var itens = await ParseItensAsync(response);
        var ids = itens.Select(item => item.GetProperty("id").GetString()).ToList();

        Assert.Equal([exato.Id, negociadoZ.Id, naoNegociadoA.Id, naoNegociadoB.Id], ids);
    }
}
