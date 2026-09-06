using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Extensions;

// PADROES.md §10.18: AddDbContextCheck<T> só confere CanConnectAsync() — não confere se as
// migrations foram aplicadas nem se o schema físico ainda bate com o modelo. Este check soma-se ao
// AddDbContextCheck e cobre os dois riscos que a §10.18 nomeia:
//
//   1. Migration PULADA (feature de migração desligada, chamada removida por regressão):
//      GetPendingMigrationsAsync() compara `__EFMigrationsHistory` com as migrations do assembly.
//   2. DRIFT MANUAL (tabela dropada por fora, com o histórico intacto): GetPendingMigrationsAsync()
//      sozinho NÃO detecta isso — ele só lê a tabela de histórico, nunca o catálogo do Postgres. Por
//      isso a segunda sonda confere, via `to_regclass`, se toda tabela que o MODELO do EF conhece
//      (`db.Model.GetEntityTypes()`) ainda existe fisicamente. A lista de tabelas é derivada do
//      modelo, não escrita à mão, porque uma lista manual desatualiza em silêncio quando uma tabela
//      nova entrar (F3) e ninguém lembrar de somá-la aqui.
//   3. TRIGGER DE IMUTABILIDADE DROPADA POR FORA: provado em revisão adversarial que dropar
//      `trg_operacoes_imutavel` por fora deixava /health/ready em 200 e o UPDATE/DELETE que antes
//      falhava passava a funcionar em silêncio — a única guarda que impede corrupção irreversível em
//      `operacoes` some sem ninguém notar. Por isso a terceira sonda confere, via `pg_trigger` +
//      `pg_class`, que toda trigger que o MODELO do EF conhece (`db.Model.GetEntityTypes()
//      .SelectMany(e => e.GetDeclaredTriggers())`, preenchido por `HasTrigger` em
//      OperacaoConfiguration) ainda existe fisicamente — mesmo racional de TabelasAusentesAsync
//      abaixo: a lista vem do modelo, não é um nome literal escrito à mão, para não desatualizar em
//      silêncio quando uma trigger nova entrar e ninguém lembrar de somá-la aqui
//      (`tgisinternal = false` para não casar as triggers internas que o Postgres cria para
//      constraints, ex.: FK).
//
// O que este check NÃO cobre: colunas, índices e CHECKs alterados ou removidos por fora mantendo a
// tabela — só a existência da tabela e da trigger de imutabilidade são verificadas. Falha de
// qualquer natureza (banco indisponível, credencial inválida, erro na consulta) vira Unhealthy,
// nunca deixa a exceção vazar.
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
            // Falha do banco (indisponível, credencial inválida, etc.) vira Unhealthy — nunca deixa a
            // exceção vazar para fora do healthcheck.
            return HealthCheckResult.Unhealthy("Falha ao verificar migrations pendentes e schema.", ex);
        }
    }

    // Uma única consulta para todas as tabelas do modelo (to_regclass + unnest), em vez de uma
    // consulta por tabela — a lista vem de db.Model, nunca escrita à mão.
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

        // O array vai embrulhado em `object[] { tabelas }` de propósito: `SqlQueryRaw(string,
        // params object[])` com um `string[]` passado direto sofre covariância de array e é
        // espalhado como um parâmetro POR TABELA (p0, p1, ...) em vez de virar o único parâmetro
        // `{0}` do tipo `text[]` que o `unnest` espera — resultado, "function unnest(text) does not
        // exist". Provado batendo contra Postgres real antes desta correção.
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

    // A lista de triggers esperadas vem de db.Model (via HasTrigger em OperacaoConfiguration), não é
    // escrita à mão — mesmo racional de TabelasAusentesAsync acima. GetDeclaredTriggers() dá o nome da
    // trigger (GetDatabaseName()) e a tabela onde ela foi declarada (GetTableName()); combinamos os
    // dois numa única string "tabela:trigger" para comparar contra o catálogo físico do Postgres com
    // uma única consulta, em vez de uma por trigger.
    //
    // HasTrigger é só metadado — não confere se a trigger existe de fato no banco (ver comentário em
    // OperacaoConfiguration sobre o porquê de ser seguro adotá-lo: não muda schema nem SaveChanges no
    // Npgsql). Por isso esta sonda contra pg_trigger continua necessária mesmo com HasTrigger presente:
    // ela é quem prova a existência física, HasTrigger só declara a intenção. tgisinternal = false
    // exclui as triggers que o próprio Postgres cria por baixo dos panos para sustentar constraints
    // (ex.: FK), que não são o que esta sonda quer enxergar.
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
