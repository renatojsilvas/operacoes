using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.Infrastructure.Catalogo;

public sealed class HubCatalogoClient(HttpClient httpClient, ILogger<HubCatalogoClient> logger) : IHubCatalogoClient
{
    private const int PageSize = 500;
    private const int MaxPaginas = 100;
    private const string InstrumentsPath = "v1/instruments";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(termo);

        var itens = new List<InstrumentoCatalogo>();
        var page = 1;
        int? totalCount = null;
        var totalBruto = 0;
        var coletaCompleta = false;

        while (page <= MaxPaginas)
        {
            var requestUri = BuildRequestUri(termo, page);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(requestUri, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao consultar o catálogo do Hub para o termo {Termo}.", termo);
                return CatalogoErrors.HubIndisponivel;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError(
                        "Hub respondeu {StatusCode} para {RequestUri} ao buscar o termo {Termo}.",
                        (int)response.StatusCode, requestUri, termo);
                    return CatalogoErrors.HubIndisponivel;
                }

                if (page == 1
                    && response.Headers.TryGetValues("X-Total-Count", out var totalCountValues)
                    && int.TryParse(
                        totalCountValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
                    && total > 0)
                {
                    totalCount = total;
                }

                List<HubInstrumentoResponse>? pagina;
                try
                {
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    pagina = await JsonSerializer.DeserializeAsync<List<HubInstrumentoResponse>>(stream, JsonOptions, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex, "Falha ao ler/desserializar a resposta do Hub ao buscar o termo {Termo}.", termo);
                    return CatalogoErrors.HubIndisponivel;
                }

                if (pagina is null)
                {
                    return CatalogoErrors.HubIndisponivel;
                }

                totalBruto += pagina.Count;

                var descartados = 0;
                foreach (var itemBruto in pagina)
                {
                    if (string.IsNullOrWhiteSpace(itemBruto.Id))
                    {
                        descartados++;
                        continue;
                    }

                    itens.Add(ParaInstrumentoCatalogo(itemBruto));
                }

                if (descartados > 0)
                {
                    logger.LogWarning(
                        "Descartados {Descartados} itens do catálogo do Hub sem id válido para o termo {Termo}.",
                        descartados, termo);
                }

                if (totalCount is int totalAnunciadoNestaPagina && totalBruto > totalAnunciadoNestaPagina)
                {
                    logger.LogWarning(
                        "Hub anunciou X-Total-Count {TotalAnunciado} mas a coleta já reuniu {Coletados} itens para o " +
                        "termo {Termo}; descartando o header inconsistente e seguindo a paginação pelo tamanho de página.",
                        totalAnunciadoNestaPagina, totalBruto, termo);
                    totalCount = null;
                }

                if (pagina.Count == 0 || pagina.Count < PageSize)
                {
                    coletaCompleta = true;
                    break;
                }

                if (totalCount is int totalConhecido && totalBruto == totalConhecido)
                {
                    coletaCompleta = true;
                    break;
                }
            }

            page++;
        }

        if (!coletaCompleta)
        {
            logger.LogError(
                "Coleta do catálogo do Hub para o termo {Termo} atingiu o teto de {MaxPaginas} páginas sem concluir; " +
                "descartando resultado parcial.",
                termo, MaxPaginas);
            return CatalogoErrors.HubColetaIncompleta;
        }

        if (totalCount is int totalAnunciado && totalBruto < totalAnunciado)
        {
            logger.LogError(
                "Hub anunciou X-Total-Count {TotalAnunciado} mas a coleta reuniu apenas {Coletados} itens para o " +
                "termo {Termo}; descartando resultado truncado.",
                totalAnunciado, totalBruto, termo);
            return CatalogoErrors.HubColetaIncompleta;
        }

        return Result<IReadOnlyList<InstrumentoCatalogo>>.Success(itens);
    }

    private static InstrumentoCatalogo ParaInstrumentoCatalogo(HubInstrumentoResponse resposta) =>
        new(resposta.Id, resposta.Classe, resposta.NomeExibicao, resposta.Vencido);

    private static string BuildRequestUri(string termo, int page) =>
        $"{InstrumentsPath}?query={Uri.EscapeDataString(termo)}&page={page}&pageSize={PageSize}";
}
