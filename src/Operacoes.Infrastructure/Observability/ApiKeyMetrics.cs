using Prometheus;

namespace Operacoes.Infrastructure.Observability;

public sealed class ApiKeyMetrics : IApiKeyMetrics
{
    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total",
        "Total de requisições ao Operações por desfecho da autenticação via API key (authorized|unauthorized).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    public void RecordRequest(string outcome) => ApiKeyRequestsTotal.WithLabels(outcome).Inc();
}
