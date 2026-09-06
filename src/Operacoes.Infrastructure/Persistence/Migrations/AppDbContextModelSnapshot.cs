using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Operacoes.Infrastructure.Persistence;

#nullable disable
namespace Operacoes.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);
            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
            modelBuilder.Entity("Operacoes.Domain.Operacoes.Operacao", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");
                    b.Property<string>("ClienteId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("cliente_id");
                    b.Property<DateOnly>("DataEvento")
                        .HasColumnType("date")
                        .HasColumnName("data_evento");
                    b.Property<string>("EstornaOperacaoId")
                        .HasColumnType("text")
                        .HasColumnName("estorna_operacao_id");
                    b.Property<string>("InstrumentoId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("instrumento_id");
                    b.Property<decimal>("Quantidade")
                        .HasPrecision(18, 8)
                        .HasColumnType("numeric(18,8)")
                        .HasColumnName("quantidade");
                    b.Property<DateTimeOffset>("RegistradoEm")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("registrado_em")
                        .HasDefaultValueSql("now()");
                    b.Property<string>("Tipo")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("operacao");
                    b.Property<decimal>("ValorFinanceiro")
                        .HasPrecision(18, 2)
                        .HasColumnType("numeric(18,2)")
                        .HasColumnName("valor_financeiro");
                    b.HasKey("Id");
                    b.HasAlternateKey("Id", "ClienteId", "InstrumentoId")
                        .HasName("ux_operacoes_id_cliente_instrumento");
                    b.HasIndex("EstornaOperacaoId")
                        .IsUnique()
                        .HasDatabaseName("ix_operacoes_estorna_unico")
                        .HasFilter("estorna_operacao_id IS NOT NULL");
                    b.HasIndex("ClienteId", "DataEvento")
                        .IsDescending(false, true)
                        .HasDatabaseName("ix_operacoes_cliente");
                    b.HasIndex("EstornaOperacaoId", "ClienteId", "InstrumentoId")
                        .HasDatabaseName("ix_operacoes_estorna_cliente_instrumento");
                    b.ToTable("operacoes", null, t =>
                        {
                            t.HasTrigger("trg_operacoes_imutavel");
                            t.HasCheckConstraint("ck_operacoes_estorno_coerente", "(operacao = 'estorno') = (estorna_operacao_id IS NOT NULL)");
                            t.HasCheckConstraint("ck_operacoes_estorno_nao_auto", "estorna_operacao_id IS NULL OR estorna_operacao_id <> id");
                            t.HasCheckConstraint("ck_operacoes_operacao_valida", "operacao IN ('aplicacao', 'resgate', 'aporte', 'estorno')");
                        });
                });
            modelBuilder.Entity("Operacoes.Domain.Outbox.OutboxMessage", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id");
                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));
                    b.Property<DateTimeOffset>("CriadoEm")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("criado_em");
                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("payload");
                    b.Property<DateTimeOffset?>("PublicadoEm")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("publicado_em");
                    b.Property<string>("RoutingKey")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("routing_key");
                    b.Property<string>("Tipo")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("tipo");
                    b.HasKey("Id");
                    b.HasIndex("Id")
                        .HasDatabaseName("ix_outbox_pendentes")
                        .HasFilter("publicado_em IS NULL");
                    b.ToTable("outbox", (string)null);
                });
            modelBuilder.Entity("Operacoes.Domain.Operacoes.Operacao", b =>
                {
                    b.HasOne("Operacoes.Domain.Operacoes.Operacao", null)
                        .WithMany()
                        .HasForeignKey("EstornaOperacaoId", "ClienteId", "InstrumentoId")
                        .HasPrincipalKey("Id", "ClienteId", "InstrumentoId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .HasConstraintName("FK_operacoes_operacoes_estorna_operacao_id");
                });
#pragma warning restore 612, 618
        }
    }
}
