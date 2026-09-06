using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Infrastructure.Persistence.Repositories;

public sealed class OperacaoWriteRepository(AppDbContext dbContext) : IOperacaoWriteRepository
{
    public async Task<Result> AdicionarAsync(Operacao operacao, CancellationToken ct)
    {
        await dbContext.Operacoes.AddAsync(operacao, ct);
        return Result.Success();
    }
}
