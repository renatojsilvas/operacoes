using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operacoes.Infrastructure.Persistence.Migrations
{
    public partial class AdicionaValorOrigemSaldoOperacoes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM operacoes WHERE operacao IN ('aplicacao', 'aporte');
                    IF n > 0 THEN
                        RAISE EXCEPTION 'AdicionaValorOrigemSaldoOperacoes: % linha(s) de aplicacao/aporte ja existem e nao tem valor_origem_saldo, que passa a ser obrigatorio para esses tipos. A tabela e append-only e o valor NAO pode ser inferido (0 afirmaria que foi dinheiro de fora, e isso e exatamente o palpite que o campo existe para evitar). Decida o que fazer com essas linhas antes de aplicar esta migration.', n;
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "valor_origem_saldo",
                table: "operacoes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operacoes_valor_origem_saldo_coerente",
                table: "operacoes",
                sql: "(operacao IN ('aplicacao', 'aporte')) = (valor_origem_saldo IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operacoes_valor_origem_saldo_faixa",
                table: "operacoes",
                sql: "valor_origem_saldo IS NULL OR (valor_origem_saldo >= 0 AND valor_origem_saldo <= valor_financeiro)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operacoes_valor_origem_saldo_coerente",
                table: "operacoes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operacoes_valor_origem_saldo_faixa",
                table: "operacoes");

            migrationBuilder.DropColumn(
                name: "valor_origem_saldo",
                table: "operacoes");
        }
    }
}
