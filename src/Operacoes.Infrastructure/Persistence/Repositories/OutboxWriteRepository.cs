using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Operacoes.Infrastructure.Persistence.Repositories;

public sealed class OutboxWriteRepository(AppDbContext dbContext, ILogger<OutboxWriteRepository> logger) : IOutboxWriteRepository
{
    public async Task<Result> AdicionarAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        await dbContext.OutboxMessages.AddAsync(mensagem, ct);
        return Result.Success();
    }

    public async Task<Result<int>> MarcarPublicadosAsync(IReadOnlyList<long> ids, DateTimeOffset publicadoEm, CancellationToken ct)
    {
        try
        {
            var afetados = await dbContext.OutboxMessages
                .Where(m => ids.Contains(m.Id) && m.PublicadoEm == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.PublicadoEm, publicadoEm), ct);

            return Result<int>.Success(afetados);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao marcar {Quantidade} mensagem(ns) da outbox como publicadas", ids.Count);
            return Result<int>.Failure(OutboxErrors.FalhaAoMarcarPublicado);
        }
    }
}
