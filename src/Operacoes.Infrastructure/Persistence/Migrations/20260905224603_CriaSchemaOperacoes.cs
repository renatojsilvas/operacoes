﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Operacoes.Infrastructure.Persistence.Migrations
{

    public partial class CriaSchemaOperacoes : Migration
    {

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

                    table.UniqueConstraint("ux_operacoes_id_cliente_instrumento", x => new { x.id, x.cliente_id, x.instrumento_id });
                    table.CheckConstraint("ck_operacoes_estorno_coerente", "(operacao = 'estorno') = (estorna_operacao_id IS NOT NULL)");
                    table.CheckConstraint("ck_operacoes_estorno_nao_auto", "estorna_operacao_id IS NULL OR estorna_operacao_id <> id");

                    table.CheckConstraint("ck_operacoes_operacao_valida", "operacao IN ('aplicacao', 'resgate', 'aporte', 'estorno')");

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
