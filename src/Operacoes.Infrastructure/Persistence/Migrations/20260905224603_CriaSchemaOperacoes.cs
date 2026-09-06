using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Operacoes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CriaSchemaOperacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operacoes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    cliente_id = table.Column<string>(type: "text", nullable: false),
                    instrumento_id = table.Column<string>(type: "text", nullable: false),
                    operacao = table.Column<string>(type: "text", nullable: false),
                    quantidade = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    valor_financeiro = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    data_evento = table.Column<DateOnly>(type: "date", nullable: false),
                    registrado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    estorna_operacao_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operacoes", x => x.id);

                    // ux_operacoes_id_cliente_instrumento existe só para sustentar a FK composta
                    // abaixo (não é um índice de negócio: inclui `id`, a PK). ck_operacoes_estorno_nao_auto
                    // e ck_operacoes_estorno_coerente fecham o buraco provado pelo revisor: sem eles,
                    // um INSERT com estorna_operacao_id = id passava (a FK auto-referente se
                    // satisfaz sozinha), consumia o slot do ix_operacoes_estorna_unico daquela linha
                    // e — como UPDATE/DELETE são bloqueados pela trigger de imutabilidade logo
                    // abaixo — tornava o estorno legítimo daquela operação impossível para sempre.
                    //
                    // Ausência deliberada: nenhum CHECK aqui impede estorno de estorno (op-C estorna
                    // op-B, que estornou op-A). É a única saída para corrigir um estorno que entrou
                    // errado, já que UPDATE e DELETE são sempre bloqueados (§6.1 camada 3 / ADR-10 —
                    // correção de inconsistência é sempre por INSERT de estorno). Bloquear a cadeia
                    // fecharia a última porta de correção; não "conserte" isso depois.
                    table.UniqueConstraint("ux_operacoes_id_cliente_instrumento", x => new { x.id, x.cliente_id, x.instrumento_id });
                    table.CheckConstraint("ck_operacoes_estorno_coerente", "(operacao = 'estorno') = (estorna_operacao_id IS NOT NULL)");
                    table.CheckConstraint("ck_operacoes_estorno_nao_auto", "estorna_operacao_id IS NULL OR estorna_operacao_id <> id");

                    // PADROES.md §10.21 (segunda revisão adversarial): em tabela append-only, o lado
                    // estrito é o lado reversível — sem este CHECK, um INSERT com operacao =
                    // 'valor-que-nao-existe' e estorna_operacao_id NULL era aceito e gravava lixo
                    // permanente numa tabela que UPDATE/DELETE nunca corrige. A lista abaixo TEM QUE
                    // bater exatamente com TipoOperacao.All (Operacoes.Domain) — mexer num lado sem o
                    // outro quebra a gravação.
                    table.CheckConstraint("ck_operacoes_operacao_valida", "operacao IN ('aplicacao', 'resgate', 'aporte', 'estorno')");

                    // FK composta (estorna_operacao_id, cliente_id, instrumento_id) -> operacoes(id,
                    // cliente_id, instrumento_id): além de existir, a operação original referenciada
                    // precisa ser do MESMO cliente e do MESMO instrumento — um estorno não "conserta"
                    // a operação de outro cliente ou de outro instrumento. MATCH SIMPLE (default do
                    // Postgres) faz a FK ser ignorada quando estorna_operacao_id IS NULL, que é
                    // exatamente o comportamento desejado para toda operação que não é estorno.
                    table.ForeignKey(
                        name: "FK_operacoes_operacoes_estorna_operacao_id",
                        columns: x => new { x.estorna_operacao_id, x.cliente_id, x.instrumento_id },
                        principalTable: "operacoes",
                        principalColumns: new[] { "id", "cliente_id", "instrumento_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    routing_key = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publicado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operacoes_cliente",
                table: "operacoes",
                columns: new[] { "cliente_id", "data_evento" },
                descending: new[] { false, true });

            // Índice de cobertura da FK composta acima (estorna_operacao_id, cliente_id,
            // instrumento_id): o EF Core o criaria de qualquer jeito, sem nome explícito, para
            // sustentar o lookup da FK. Nomeado aqui só para respeitar a convenção do repo
            // (ix_tabela_colunas, §3 do PADROES.md) — não é único, não é índice de negócio.
            migrationBuilder.CreateIndex(
                name: "ix_operacoes_estorna_cliente_instrumento",
                table: "operacoes",
                columns: new[] { "estorna_operacao_id", "cliente_id", "instrumento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operacoes_estorna_unico",
                table: "operacoes",
                column: "estorna_operacao_id",
                unique: true,
                filter: "estorna_operacao_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pendentes",
                table: "outbox",
                column: "id",
                filter: "publicado_em IS NULL");

            // Imutabilidade de `operacoes` (§9 do ARQUITETURA.md / decisão do F2, ver docs/ROADMAP.md):
            // nenhuma linha sofre UPDATE ou DELETE — correção é por INSERT de uma nova operação de
            // estorno. A guarda fica no banco, não só na disciplina de revisão de código, porque é a
            // role `operacoes` (dona do database, ver infra/postgres/sql/operacoes-role.sql) quem roda
            // as migrations no boot com essa mesma role — um REVOKE UPDATE/DELETE seria decoração e
            // quebraria a própria migration se algum dia precisar reescrever dados.
            //
            // TRUNCATE não dispara trigger FOR EACH ROW: é intencional, não um buraco na guarda — é o
            // que mantém a limpeza de fixture de teste funcionando (TRUNCATE operacoes entre testes).
            //
            // Fuga legítima: uma migration futura que precise de backfill/correção em massa usa
            // `ALTER TABLE operacoes DISABLE TRIGGER trg_operacoes_imutavel` dentro da própria migration,
            // faz o UPDATE, e reabilita com `ENABLE TRIGGER` antes de terminar.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION operacoes_bloqueia_update_delete() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'operacoes é append-only: % não é permitido (id=%). Correção é por INSERT de uma operação de estorno.',
                        TG_OP, COALESCE(OLD.id, 'desconhecido');
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_operacoes_imutavel
                BEFORE UPDATE OR DELETE ON operacoes
                FOR EACH ROW EXECUTE FUNCTION operacoes_bloqueia_update_delete();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_operacoes_imutavel ON operacoes;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS operacoes_bloqueia_update_delete();");

            migrationBuilder.DropTable(
                name: "operacoes");

            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
