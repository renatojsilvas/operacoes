using Operacoes.Application.Common.Interfaces;
using Operacoes.Infrastructure.Observability;
using Prometheus;

namespace Operacoes.Infrastructure.Tests.Observability;

public sealed class BusinessMetricsTests
{
    private static readonly Counter RelayCiclosTotal = Metrics.CreateCounter(
        "operacoes_relay_ciclos_total", "help", new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Counter RelayEventosPublicadosTotal = Metrics.CreateCounter(
        "operacoes_relay_eventos_publicados_total", "help");

    private static readonly Gauge OutboxPendentes = Metrics.CreateGauge(
        "operacoes_outbox_pendentes", "help");

    private static readonly Gauge OutboxPendenteMaisAntigaSegundos = Metrics.CreateGauge(
        "operacoes_outbox_pendente_mais_antiga_segundos", "help");

    private readonly BusinessMetrics _metrics = new();

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public void RecordCicloRelay_IncrementaOContadorParaODesfecho(string outcome)
    {
        var antes = RelayCiclosTotal.WithLabels(outcome).Value;

        _metrics.RecordCicloRelay(outcome);

        var depois = RelayCiclosTotal.WithLabels(outcome).Value;
        Assert.Equal(1, depois - antes);
    }

    [Fact]
    public void RecordEventosPublicados_IncrementaOContadorNaQuantidadeInformada()
    {
        var antes = RelayEventosPublicadosTotal.Value;

        _metrics.RecordEventosPublicados(7);

        var depois = RelayEventosPublicadosTotal.Value;
        Assert.Equal(7, depois - antes);
    }

    [Fact]
    public void RecordOutboxBacklog_AtualizaOsGaugesDePendentesEIdade()
    {
        _metrics.RecordOutboxBacklog(42, 12.5);

        Assert.Equal(42, OutboxPendentes.Value);
        Assert.Equal(12.5, OutboxPendenteMaisAntigaSegundos.Value);
    }
}
