using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Operacoes.Application.Catalogo;
using Operacoes.Domain.Common;

namespace Operacoes.Infrastructure.Catalogo;

public sealed class HubCatalogoClient(HttpClient httpClient, ILogger<HubCatalogoClient> logger) : IHubCatalogoClient
{

    private const int PageSize = 500;
    private const string InstrumentsPath = "v1/instruments";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<bool>> InstrumentoExisteAsync(string instrumentoId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentoId);

        var page = 1;
        var itemsFetched = 0;
        var hasTotalCount = false;
        var totalCount = 0;

        do
        {
            var requestUri = BuildRequestUri(instrumentoId, page);

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

                logger.LogError(ex, "Falha ao consultar o catálogo do Hub para o instrumento {InstrumentoId}.", instrumentoId);
                return CatalogoErrors.HubIndisponivel;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {

                    logger.LogError(
                        "Hub respondeu {StatusCode} para {RequestUri} ao validar o instrumento {InstrumentoId}.",
                        (int)response.StatusCode, requestUri, instrumentoId);
                    return CatalogoErrors.HubIndisponivel;
                }

                if (page == 1)
                {
                    hasTotalCount = response.Headers.TryGetValues("X-Total-Count", out var totalCountValues)
                        && int.TryParse(
                            totalCountValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalCount);
                }

                List<HubInstrumentoResponse>? instrumentos;
                try
                {
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    instrumentos = await JsonSerializer.DeserializeAsync<List<HubInstrumentoResponse>>(stream, JsonOptions, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex, "Falha ao ler/desserializar a resposta do Hub ao validar o instrumento {InstrumentoId}.", instrumentoId);
                    return CatalogoErrors.HubIndisponivel;
                }

                if (instrumentos is null)
                {
                    return CatalogoErrors.HubIndisponivel;
                }

                if (instrumentos.Any(i => string.Equals(i.Id, instrumentoId, StringComparison.Ordinal)))
                {
                    return true;
                }

                itemsFetched += instrumentos.Count;

                if (instrumentos.Count == 0)
                {
                    break;
                }
            }

            page++;
        }
        while (hasTotalCount && itemsFetched < totalCount);

        return false;
    }

    private static string BuildRequestUri(string instrumentoId, int page) =>
        $"{InstrumentsPath}?query={Uri.EscapeDataString(instrumentoId)}&page={page}&pageSize={PageSize}";
}
