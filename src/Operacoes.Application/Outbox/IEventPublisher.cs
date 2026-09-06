using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Outbox;

public interface IEventPublisher
{
    Task<Result<int>> PublicarAsync(IReadOnlyList<OutboxPendente> lote, CancellationToken ct);
}
