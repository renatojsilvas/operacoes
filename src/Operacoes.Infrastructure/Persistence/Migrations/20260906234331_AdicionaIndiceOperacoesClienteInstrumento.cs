using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operacoes.Infrastructure.Persistence.Migrations
{
    public partial class AdicionaIndiceOperacoesClienteInstrumento : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_operacoes_cliente_instrumento",
                table: "operacoes",
                columns: new[] { "cliente_id", "instrumento_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_operacoes_cliente_instrumento",
                table: "operacoes");
        }
    }
}
