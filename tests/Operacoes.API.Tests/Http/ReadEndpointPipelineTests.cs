using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Operacoes.API.Http;
using Operacoes.Infrastructure.Http;

namespace Operacoes.API.Tests.Http;

public sealed class ReadEndpointPipelineTests
{
    private const string CorpoProbe = "[\"ok\"]";

    private static async Task<ProbeHost> CriarHostAsync(
        IContentVersionProvider versionProvider,
        IDictionary<string, string?>? configuracaoExtra = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        if (configuracaoExtra is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuracaoExtra);
        }

        builder.Services.AddSingleton(versionProvider);

        var app = builder.Build();
        app.MapReadGet("/probe", () => Results.Text(CorpoProbe, "application/json", statusCode: StatusCodes.Status200OK));

        await app.StartAsync();

        return new ProbeHost(app);
    }

    private sealed class ProbeHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_PrimeiraChamada_Devolve200ComEtagECacheControlPrivadoENuncaPublic()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        var resposta = await host.Client.GetAsync("/probe");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.NotNull(resposta.Headers.ETag);
        Assert.NotNull(resposta.Headers.CacheControl);
        Assert.False(
            resposta.Headers.CacheControl!.Public,
            "Cache-Control nao pode ser 'public': a resposta deste endpoint varia por cliente.");
        Assert.True(resposta.Headers.CacheControl.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), resposta.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Get_ComCacheControlConfigurado_UsaOValorInjetadoEmVezDoDefault()
    {
        await using var host = await CriarHostAsync(
            new FakeContentVersionProvider("versao-1"),
            new Dictionary<string, string?> { ["Http:CacheControl"] = "private, max-age=30" });

        var resposta = await host.Client.GetAsync("/probe");

        Assert.Equal(TimeSpan.FromSeconds(30), resposta.Headers.CacheControl!.MaxAge);
    }

    [Fact]
    public async Task Get_ComIfNoneMatchIgualAoEtagAtual_Devolve304SemCorpoESemCacheControl()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        var primeira = await host.Client.GetAsync("/probe");
        var etag = primeira.Headers.ETag!.Tag;

        using var condicional = new HttpRequestMessage(HttpMethod.Get, "/probe");
        condicional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var segunda = await host.Client.SendAsync(condicional);

        Assert.Equal(HttpStatusCode.NotModified, segunda.StatusCode);
        Assert.Null(segunda.Headers.CacheControl);
        var corpo = await segunda.Content.ReadAsStringAsync();
        Assert.Empty(corpo);
    }

    [Fact]
    public async Task Head_Devolve200SemCorpoComOMesmoEtagDoGet()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        var get = await host.Client.GetAsync("/probe");

        using var head = new HttpRequestMessage(HttpMethod.Head, "/probe");
        var respostaHead = await host.Client.SendAsync(head);

        Assert.Equal(HttpStatusCode.OK, respostaHead.StatusCode);
        var corpo = await respostaHead.Content.ReadAsStringAsync();
        Assert.Empty(corpo);
        Assert.Equal(get.Headers.ETag!.Tag, respostaHead.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Options_Devolve204ComAllowContendoGetHeadEOptions()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        using var request = new HttpRequestMessage(HttpMethod.Options, "/probe");
        var resposta = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var allow = string.Join(",", resposta.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Delete_Devolve405ComAllowContendoGetHeadEOptions()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/probe");
        var resposta = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resposta.StatusCode);
        var allow = string.Join(",", resposta.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Get_ComQueryStringsComMesmasChavesEmOrdemDiferente_GeraOMesmoEtag()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        var respostaA = await host.Client.GetAsync("/probe?a=1&b=2");
        var respostaB = await host.Client.GetAsync("/probe?b=2&a=1");

        Assert.Equal(respostaA.Headers.ETag!.Tag, respostaB.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Get_ComQueryStringsDiferentes_GeraEtagsDiferentes()
    {
        await using var host = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));

        var respostaA = await host.Client.GetAsync("/probe?clienteId=a");
        var respostaB = await host.Client.GetAsync("/probe?clienteId=b");

        Assert.NotEqual(respostaA.Headers.ETag!.Tag, respostaB.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Get_QuandoAVersaoDeConteudoMuda_OEtagMuda()
    {
        await using var hostComVersao1 = await CriarHostAsync(new FakeContentVersionProvider("versao-1"));
        await using var hostComVersao2 = await CriarHostAsync(new FakeContentVersionProvider("versao-2"));

        var primeira = await hostComVersao1.Client.GetAsync("/probe");
        var segunda = await hostComVersao2.Client.GetAsync("/probe");

        Assert.NotEqual(primeira.Headers.ETag!.Tag, segunda.Headers.ETag!.Tag);
    }
}
