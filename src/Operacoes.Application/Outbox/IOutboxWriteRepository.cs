using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Outbox;

public interface IOutboxWriteRepository
{
    Task<Result> AdicionarAsync(OutboxMessage mensagem, CancellationToken ct);
}
