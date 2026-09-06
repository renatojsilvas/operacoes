namespace Operacoes.Application.Common.Interfaces;

public interface IBusinessMetrics
{
    void RecordCicloRelay(string outcome);

    void RecordEventosPublicados(long quantidade);

    void RecordOutboxBacklog(long pendentes, double idadeSegundos);
}
