using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Extensions;

public sealed class PendingMigrationsHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pendentes = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pendentes.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"{pendentes.Count} migration(ns) pendente(s): {string.Join(", ", pendentes)}.");
            }

            var tabelasAusentes = await TabelasAusentesAsync(cancellationToken);

            if (tabelasAusentes.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    "Tabela(s) do modelo ausente(s) no schema físico (drift manual): " +
                    $"{string.Join(", ", tabelasAusentes)}.");
            }

            var triggersAusentes = await TriggersAusentesAsync(cancellationToken);

            if (triggersAusentes.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    "Trigger(s) do modelo ausente(s) no schema físico (drift manual): " +
                    $"{string.Join(", ", triggersAusentes)}. A guarda de imutabilidade da tabela pode " +
                    "ter sido removida por fora, com UPDATE/DELETE deixando de ser bloqueados.");
            }

            return HealthCheckResult.Healthy(
                "Nenhuma migration pendente; tabelas e triggers do modelo presentes no schema.");
        }
        catch (Exception ex)
        {

            return HealthCheckResult.Unhealthy("Falha ao verificar migrations pendentes e schema.", ex);
        }
    }

    private async Task<IReadOnlyList<string>> TabelasAusentesAsync(CancellationToken cancellationToken)
    {
        var tabelas = db.Model.GetEntityTypes()
            .Select(entidade => entidade.GetTableName())
            .Where(nome => nome is not null)
            .Select(nome => nome!)
            .Distinct()
            .ToArray();

        if (tabelas.Length == 0)
        {
            return [];
        }

        return await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT t.nome
                FROM unnest({0}) AS t(nome)
                WHERE to_regclass('public.' || t.nome) IS NULL
                """,
                new object[] { tabelas })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> TriggersAusentesAsync(CancellationToken cancellationToken)
    {
        var triggersEsperados = db.Model.GetEntityTypes()
            .SelectMany(entidade => entidade.GetDeclaredTriggers())
            .Select(trigger => $"{trigger.GetTableName()}:{trigger.GetDatabaseName()}")
            .Distinct()
            .ToArray();

        if (triggersEsperados.Length == 0)
        {
            return [];
        }

        var triggersExistentes = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname || ':' || trg.tgname AS nome
                FROM pg_trigger trg
                JOIN pg_class c ON c.oid = trg.tgrelid
                WHERE trg.tgisinternal = false
                """)
            .ToListAsync(cancellationToken);

        return triggersEsperados.Except(triggersExistentes).ToList();
    }
}
