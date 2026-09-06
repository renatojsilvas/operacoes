using System.Net;
using System.Net.Http.Headers;

namespace Operacoes.Infrastructure.Tests.Catalogo;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<Uri> RequestedUris { get; } = [];

    public FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public FakeHttpMessageHandler Enqueue(HttpResponseMessage response) => Enqueue(_ => response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"Nenhuma resposta enfileirada para {request.Method} {request.RequestUri}.");
        }

        return Task.FromResult(_responders.Dequeue()(request));
    }

    public static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode, string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    public static HttpResponseMessage BrokenBodyResponse(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new BrokenStreamHttpContent(),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return response;
    }

    private sealed class BrokenStreamHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("Conexão encerrada no meio do corpo.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            throw new IOException("Conexão encerrada no meio do corpo.");
    }
}
