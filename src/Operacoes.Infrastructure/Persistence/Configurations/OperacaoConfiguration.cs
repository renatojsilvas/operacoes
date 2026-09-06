using Operacoes.Domain.Operacoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Operacoes.Infrastructure.Persistence.Configurations;

// cliente_id é identidade de outro contexto: grava-se cru. Operações não tem, e não terá, tabela
// de clientes — sem FK, sem tabela local, sem de-para. Quem garante que o cliente existe é a borda
// autenticada, antes do POST /operacoes. Mesmo racional da §7.2/ADR-12 aplicado a instrumento_id:
// réplica local de entidade alheia é uma terceira identidade a divergir. Criar `clientes` aqui é
// defeito, não melhoria.
public sealed class OperacaoConfiguration : IEntityTypeConfiguration<Operacao>
{
    public void Configure(EntityTypeBuilder<Operacao> builder)
    {
        builder.ToTable("operacoes", t =>
        {
            // Metadado de trigger (terceira auditoria de conformidade, item 2): declara para o EF que
            // `operacoes` tem a trigger `trg_operacoes_imutavel` (criada por SQL cru na migration
            // CriaSchemaOperacoes — HasTrigger não cria trigger nenhuma, só documenta uma que já existe).
            // Verificado contra Postgres real, com spike, antes de adotar: (a) o Npgsql aceita a
            // anotação sem erro; (b) `dotnet ef migrations script` gera SQL idêntico com e sem esta
            // linha — é só metadado, não schema; (c) o SaveChanges não muda de estratégia — ver
            // HasTriggerMetadataTests (Operacoes.API.Tests). Isso importa porque no EF Core o
            // HasTrigger nasceu por causa do SQL Server: lá, declarar trigger faz o provider abandonar
            // a cláusula OUTPUT (SqlServerOutputClauseConvention, só existe no assembly do provider
            // SqlServer). O Npgsql não tem convenção equivalente — nenhuma referência a "trigger" no
            // assembly do provider — então aqui HasTrigger é só metadado, sem efeito colateral.
            // PendingMigrationsHealthCheck usa este metadado para derivar QUAL trigger sondar contra
            // pg_trigger, em vez de um nome literal hardcoded (mesmo racional de TabelasAusentesAsync
            // para tabelas) — mas a sonda contra pg_trigger continua necessária mesmo com HasTrigger
            // presente: HasTrigger registra que a trigger deveria existir, não confere que ela ainda
            // existe fisicamente no banco.
            t.HasTrigger("trg_operacoes_imutavel");

            // PADROES.md §10.21: em tabela append-only, o lado estrito é o lado reversível — sem este
            // CHECK, um INSERT com operacao = 'valor-que-nao-existe' e estorna_operacao_id NULL era
            // aceito e gravava lixo permanente numa tabela que UPDATE/DELETE nunca corrige. A lista
            // abaixo TEM QUE bater exatamente com TipoOperacao.All — mexer num lado sem o outro quebra
            // a gravação (Domínio aceita um tipo que o banco rejeita, ou o banco aceita um valor cru
            // que o Domínio nunca produziria).
            t.HasCheckConstraint(
                "ck_operacoes_operacao_valida",
                "operacao IN ('aplicacao', 'resgate', 'aporte', 'estorno')");

            // A auto-referência é o único ciclo possível no grafo de estornos (ciclo de tamanho >= 2
            // é impedido pela ordem de INSERT: uma operação só pode referenciar quem já existe).
            // Sem este CHECK, um INSERT com estorna_operacao_id = id passa (a FK se satisfaz sozinha),
            // consome o slot do ix_operacoes_estorna_unico daquela linha e — como UPDATE/DELETE são
            // bloqueados pela trigger de imutabilidade — torna o estorno legítimo dessa operação
            // impossível para sempre.
            t.HasCheckConstraint(
                "ck_operacoes_estorno_nao_auto",
                "estorna_operacao_id IS NULL OR estorna_operacao_id <> id");

            // Bicondicional (§7.1): estorno sem estorna_operacao_id não tem tradução no ref_estorno
            // do livro da Custódia; e estorna_operacao_id fora de um estorno é reuso indevido de uma
            // coluna cuja unicidade parcial (ix_operacoes_estorna_unico) já é exclusiva de estornos.
            t.HasCheckConstraint(
                "ck_operacoes_estorno_coerente",
                "(operacao = 'estorno') = (estorna_operacao_id IS NOT NULL)");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(o => o.ClienteId)
            .HasColumnName("cliente_id")
            .IsRequired();

        builder.Property(o => o.InstrumentoId)
            .HasColumnName("instrumento_id")
            .IsRequired();

        builder.Property(o => o.Tipo)
            .HasColumnName("operacao")
            .IsRequired()
            .HasConversion(
                v => v.Name,
                v => TipoOperacao.FromName(v).Value);

        builder.Property(o => o.Quantidade)
            .HasColumnName("quantidade")
            .HasPrecision(18, 8)
            .IsRequired();

        builder.Property(o => o.ValorFinanceiro)
            .HasColumnName("valor_financeiro")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(o => o.DataEvento)
            .HasColumnName("data_evento")
            .IsRequired();

        builder.Property(o => o.RegistradoEm)
            .HasColumnName("registrado_em")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(o => o.EstornaOperacaoId)
            .HasColumnName("estorna_operacao_id")
            .IsRequired(false);

        // Chave alternada que carrega cliente_id e instrumento_id junto com id: existe só para
        // sustentar a FK composta abaixo (não é exposta como índice de negócio — ver
        // NaoExisteIndiceUnicoSobreCamposDeNegocioDeOperacoes, que exige que todo índice único
        // não-primário tenha ao menos uma coluna fora dos campos de negócio; id cobre essa exigência).
        builder.HasAlternateKey(o => new { o.Id, o.ClienteId, o.InstrumentoId })
            .HasName("ux_operacoes_id_cliente_instrumento");

        // Auto-referência: a operação de estorno aponta para a operação original. FK composta (em vez
        // de simples por estorna_operacao_id -> id) para além de existir, a operação referenciada
        // precisa ser do MESMO cliente e do MESMO instrumento — um estorno não pode "corrigir" a
        // operação de outro cliente ou de outro instrumento. MATCH SIMPLE (default do Postgres) faz a
        // FK ser ignorada quando estorna_operacao_id IS NULL, que é o comportamento desejado para toda
        // operação que não é estorno. Restrict porque não faz sentido apagar uma operação original que
        // já foi estornada — mas, de todo modo, apagar não é possível: a trigger de imutabilidade
        // bloqueia DELETE em qualquer linha de `operacoes`.
        builder.HasOne<Operacao>()
            .WithMany()
            .HasPrincipalKey(o => new { o.Id, o.ClienteId, o.InstrumentoId })
            .HasForeignKey(o => new { o.EstornaOperacaoId, o.ClienteId, o.InstrumentoId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_operacoes_operacoes_estorna_operacao_id");

        builder.HasIndex(o => new { o.ClienteId, o.DataEvento })
            .IsDescending(false, true)
            .HasDatabaseName("ix_operacoes_cliente");

        // Uma operação não pode ser estornada duas vezes. Deliberadamente NÃO bloqueia estorno de
        // estorno (op-C estorna op-B, que estornou op-A): é a única saída para corrigir um estorno
        // que entrou errado, já que UPDATE e DELETE são sempre bloqueados pela trigger de
        // imutabilidade (§6.1 camada 3 / ADR-10 — correção de inconsistência é sempre por estorno).
        // Bloquear a cadeia fecharia a última porta de correção.
        builder.HasIndex(o => o.EstornaOperacaoId)
            .IsUnique()
            .HasFilter("estorna_operacao_id IS NOT NULL")
            .HasDatabaseName("ix_operacoes_estorna_unico");

        // Índice de cobertura da FK composta acima (estorna_operacao_id, cliente_id, instrumento_id):
        // o EF Core o criaria de qualquer jeito, sem nome explícito, para sustentar o lookup da FK.
        // Declarado aqui só para nomear na convenção do repo (§3 do PADROES.md) — não é índice de
        // negócio, não é único, existe puramente como apoio da FK.
        builder.HasIndex(o => new { o.EstornaOperacaoId, o.ClienteId, o.InstrumentoId })
            .HasDatabaseName("ix_operacoes_estorna_cliente_instrumento");
    }
}
