using Operacoes.Application.Common.Interfaces;
using Operacoes.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Operacoes.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    // Sem DbSets de negócio: o F1 só prova migrations-no-boot funcionando com nada
    // dentro. O F2 é quem cria as tabelas `operacoes` e `outbox` (ver docs/ROADMAP.md).

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    async Task<Result> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            return Result.Failure(DomainErrors.General.Conflict(
                "Conflito de gravação: outra execução já persistiu um registro com a mesma chave nesta janela."));
        }
    }

    void IUnitOfWork.LimparRastreamento()
    {
        ChangeTracker.Clear();
    }
}
