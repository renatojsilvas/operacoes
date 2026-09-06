using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Infrastructure.Persistence.Repositories;

public sealed class OutboxWriteRepository(AppDbContext dbContext) : IOutboxWriteRepository
{
    public async Task<Result> AdicionarAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        await dbContext.OutboxMessages.AddAsync(mensagem, ct);
        return Result.Success();
    }
}
