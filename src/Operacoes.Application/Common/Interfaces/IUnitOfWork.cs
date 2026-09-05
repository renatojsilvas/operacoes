using Operacoes.Domain.Common;

namespace Operacoes.Application.Common.Interfaces;

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    void LimparRastreamento();
}
