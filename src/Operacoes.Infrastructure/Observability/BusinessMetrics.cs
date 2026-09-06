using Operacoes.Application.Common.Interfaces;
using Prometheus;

namespace Operacoes.Infrastructure.Observability;

public sealed class BusinessMetrics : IBusinessMetrics
{
    private static readonly Counter RelayCiclosTotal = Metrics.CreateCounter(
        "operacoes_relay_ciclos_total",
        "Total de execuções do ciclo de relay da outbox, por desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    private static readonly Counter RelayEventosPublicadosTotal = Metrics.CreateCounter(
        "operacoes_relay_eventos_publicados_total",
        "Total de eventos da outbox publicados com sucesso no broker.");

    private static readonly Gauge OutboxPendentes = Metrics.CreateGauge(
        "operacoes_outbox_pendentes",
        "Quantidade de mensagens da outbox ainda não publicadas.");

    private static readonly Gauge OutboxPendenteMaisAntigaSegundos = Metrics.CreateGauge(
        "operacoes_outbox_pendente_mais_antiga_segundos",
        "Idade, em segundos, da mensagem pendente mais antiga na outbox.");

    public void RecordCicloRelay(string outcome) => RelayCiclosTotal.WithLabels(outcome).Inc();

    public void RecordEventosPublicados(long quantidade) => RelayEventosPublicadosTotal.Inc(quantidade);

    public void RecordOutboxBacklog(long pendentes, double idadeSegundos)
    {
        OutboxPendentes.Set(pendentes);
        OutboxPendenteMaisAntigaSegundos.Set(idadeSegundos);
    }
}
