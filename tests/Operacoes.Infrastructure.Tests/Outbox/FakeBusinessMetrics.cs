using Operacoes.Application.Common.Interfaces;

namespace Operacoes.Infrastructure.Tests.Outbox;

internal sealed class FakeBusinessMetrics : IBusinessMetrics
{
    public List<string> CiclosRelayRegistrados { get; } = [];

    public long EventosPublicadosRegistrados { get; private set; }

    public List<(long Pendentes, double IdadeSegundos)> BacklogRegistrado { get; } = [];

    public void RecordCicloRelay(string outcome) => CiclosRelayRegistrados.Add(outcome);

    public void RecordEventosPublicados(long quantidade) => EventosPublicadosRegistrados += quantidade;

    public void RecordOutboxBacklog(long pendentes, double idadeSegundos) =>
        BacklogRegistrado.Add((pendentes, idadeSegundos));
}
