using System.Net;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class ProblemDetailsStatusCoverageTests(ApiTestFactory factory)
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Get_RotaInexistente_Retorna404VazioSemProblemJsonPorForaDoEscopoDoF3()
    {
        var response = await _client.GetAsync("/v1/rota-que-nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
    }

    [Fact]
    public async Task Get_EmRotaDeOperacoesQueSoAceitaPost_Retorna405VazioSemProblemJsonPorForaDoEscopoDoF3()
    {
        var response = await _client.GetAsync("/v1/operacoes");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
    }
}
