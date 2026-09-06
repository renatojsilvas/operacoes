using MediatR;
using Operacoes.API.Extensions;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;

namespace Operacoes.API.Endpoints;

public static class OperacoesEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void MapOperacoesEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapPost("/operacoes", async (
                RegistrarOperacaoRequest request,
                HttpContext httpContext,
                ISender sender,
                CancellationToken ct) =>
            {

                httpContext.Response.Headers.CacheControl = "no-store";

                if (!TryGetIdempotencyKey(httpContext, out var idempotencyKey))
                {
                    return Result.Failure(RequisicaoErrors.IdempotencyKeyAusente).ToHttpResult(() => Results.Ok());
                }

                var command = new RegistrarOperacaoCommand(
                    request.ClienteId,
                    request.InstrumentoId,
                    request.Tipo,
                    request.Quantidade,
                    request.ValorFinanceiro,
                    request.DataEvento,
                    request.EstornaOperacaoId,
                    idempotencyKey);

                var result = await sender.Send(command, ct);

                return result.ToHttpResult(resultado =>
                {
                    var body = ParaResponse(resultado);

                    return resultado.Replay
                        ? Results.Ok(body)
                        : Results.Json(body, statusCode: StatusCodes.Status201Created);
                });
            })
            .WithName("RegistrarOperacao")
            .WithTags("Operacoes")
            .WithSummary("Registra uma operação de negociação (aplicação, resgate, aporte ou estorno)")
            .WithDescription(
                "Camada 2 da validação (ARQUITETURA.md §6.1, ADR-11): rejeita 400/422 na hora " +
                "(campo malformado/ausente, tipo fora do domínio, quantidade/valorFinanceiro <= 0, " +
                "dataEvento futura, instrumento inexistente no catálogo do Hub, estorno incoerente " +
                "ou com referência inválida). 503 se o Hub não responder — nunca aceita nem rejeita " +
                "como inexistente por falha de infraestrutura. Header Idempotency-Key obrigatório: " +
                "reenvio da mesma chave devolve 200 com a operação já gravada (replay), nunca " +
                "duplica. Sucesso não é cacheável (Cache-Control: no-store); sem Location (não há " +
                "GET /v1/operacoes/{id}).")
            .Produces<OperacaoResponse>(StatusCodes.Status201Created)
            .Produces<OperacaoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static bool TryGetIdempotencyKey(HttpContext httpContext, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;

        if (!httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values))
        {
            return false;
        }

        var value = values.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        idempotencyKey = value.Trim();
        return true;
    }

    private static OperacaoResponse ParaResponse(RegistrarOperacaoResultado resultado) => new(
        resultado.Id,
        resultado.ClienteId,
        resultado.InstrumentoId,
        resultado.Tipo,
        resultado.Quantidade,
        resultado.ValorFinanceiro,
        resultado.DataEvento,
        resultado.RegistradoEm,
        resultado.EstornaOperacaoId);

    private sealed record OperacaoResponse(
        string Id,
        string ClienteId,
        string InstrumentoId,
        string Tipo,
        decimal Quantidade,
        decimal ValorFinanceiro,
        DateOnly DataEvento,
        DateTimeOffset RegistradoEm,
        string? EstornaOperacaoId);

    public sealed record RegistrarOperacaoRequest
    {
        public required string ClienteId { get; init; }

        public required string InstrumentoId { get; init; }

        public required string Tipo { get; init; }

        public required decimal Quantidade { get; init; }

        public required decimal ValorFinanceiro { get; init; }

        public required DateOnly DataEvento { get; init; }

        public string? EstornaOperacaoId { get; init; }
    }
}
