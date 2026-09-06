using Operacoes.Application.Common.Interfaces;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Operacoes.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Operacao> Operacoes => Set<Operacao>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
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
            DetacharAlteracoes();
            return Result.Failure(DomainErrors.General.Conflict(
                "Conflito de gravação: outra execução já persistiu um registro com a mesma chave nesta janela."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
            && (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation
                || pg.SqlState == PostgresErrorCodes.CheckViolation))
        {
            DetacharAlteracoes();
            return Result.Failure(OperacaoErrors.EstornoReferenciaInvalida);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.NumericValueOutOfRange)
        {
            DetacharAlteracoes();
            return Result.Failure(DomainErrors.General.Unprocessable(
                "Valor numérico excede a magnitude ou a precisão suportada pela coluna."));
        }
    }

    private void DetacharAlteracoes()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
    void IUnitOfWork.LimparRastreamento()
    {
        ChangeTracker.Clear();
    }
}
