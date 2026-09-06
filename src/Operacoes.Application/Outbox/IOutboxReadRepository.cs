using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Outbox;

public interface IOutboxReadRepository
{
    Task<Result<IReadOnlyList<OutboxPendente>>> ObterPendentesAsync(int limite, CancellationToken ct);

    Task<Result<BacklogOutbox>> ObterBacklogAsync(CancellationToken ct);
}
