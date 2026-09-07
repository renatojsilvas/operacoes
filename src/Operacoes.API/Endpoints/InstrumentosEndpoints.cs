using System.Globalization;
using System.Text.Json.Serialization;
using MediatR;
using Operacoes.API.Extensions;
using Operacoes.API.Http;
using Operacoes.Application.Instrumentos;

namespace Operacoes.API.Endpoints;

public static class InstrumentosEndpoints
{
    public static void MapInstrumentosEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapReadGet("/operacoes/instrumentos", async (
                string? query,
                string? clienteId,
                HttpContext httpContext,
                ISender sender,
                CancellationToken ct,
                bool incluirVencidos = false,
                int? limit = null) =>
            {
                var result = await sender.Send(
                    new ListarInstrumentosQuery(query, clienteId, incluirVencidos, limit), ct);

                return result.ToHttpResult(resultado =>
                {
                    httpContext.Response.Headers["X-Total-Count"] =
                        resultado.TotalCount.ToString(CultureInfo.InvariantCulture);

                    return Results.Ok(resultado.Itens.Select(ParaResponse).ToList());
                });
            })
            .WithName("ListarInstrumentos")
            .WithTags("Instrumentos")
            .WithSummary("Autocomplete do catálogo de instrumentos do Hub para o registro de operações")
            .WithDescription(
                "Proxy filtrado do catálogo do Hub (ARQUITETURA §6), usado para preencher instrumentoId no " +
                "POST /v1/operacoes. incluirVencidos (default false) omite instrumentos vencidos, EXCETO o " +
                "item cujo id é match exato do query (ordinal, após trim) — esse nunca é omitido e vai " +
                "sempre em primeiro lugar, porque lançamento retroativo de título vencido é caso legítimo e " +
                "o autocomplete nunca pode transformar 'está vencido' em 'não existe' (§6.1). clienteId é " +
                "opcional; quando informado, cada item recebe jaNegociado indicando se o cliente já " +
                "registrou alguma operação com aquele instrumento — não é posição (Operações não a tem, " +
                "ADR-10); sem clienteId a propriedade jaNegociado é omitida do JSON, nunca false. Ordenação: " +
                "match exato primeiro, depois jaNegociado desc, depois id asc; o corte em limit (default 20, " +
                "máx 50) acontece por último, sobre a ordenação inteira já filtrada. X-Total-Count reflete o " +
                "total após os filtros e antes do corte. Sem paginação (page/Link) — coleção limitada por " +
                "construção (PADROES §2). 400 quando query está ausente/vazia/só espaços, ou quando limit é " +
                "<= 0. 503 quando o Hub não responde.")
            .Produces<IReadOnlyList<InstrumentoAutocompleteResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static InstrumentoAutocompleteResponse ParaResponse(InstrumentoAutocompleteItem item) => new(
        item.Id,
        item.Classe,
        item.NomeExibicao,
        item.Vencido,
        item.JaNegociado);

    private sealed record InstrumentoAutocompleteResponse(
        string Id,
        string Classe,
        string NomeExibicao,
        bool Vencido,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? JaNegociado);
}
