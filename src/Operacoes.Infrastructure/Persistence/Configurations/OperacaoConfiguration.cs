using Operacoes.Domain.Operacoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Operacoes.Infrastructure.Persistence.Configurations;

public sealed class OperacaoConfiguration : IEntityTypeConfiguration<Operacao>
{
    public void Configure(EntityTypeBuilder<Operacao> builder)
    {
        builder.ToTable("operacoes", t =>
        {

            t.HasTrigger("trg_operacoes_imutavel");

            t.HasCheckConstraint(
                "ck_operacoes_operacao_valida",
                "operacao IN ('aplicacao', 'resgate', 'aporte', 'estorno')");

            t.HasCheckConstraint(
                "ck_operacoes_estorno_nao_auto",
                "estorna_operacao_id IS NULL OR estorna_operacao_id <> id");

            t.HasCheckConstraint(
                "ck_operacoes_estorno_coerente",
                "(operacao = 'estorno') = (estorna_operacao_id IS NOT NULL)");

            t.HasCheckConstraint(
                "ck_operacoes_valor_origem_saldo_coerente",
                "(operacao IN ('aplicacao', 'aporte')) = (valor_origem_saldo IS NOT NULL)");

            t.HasCheckConstraint(
                "ck_operacoes_valor_origem_saldo_faixa",
                "valor_origem_saldo IS NULL OR (valor_origem_saldo >= 0 AND valor_origem_saldo <= valor_financeiro)");
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

        builder.Property(o => o.ValorOrigemSaldo)
            .HasColumnName("valor_origem_saldo")
            .HasPrecision(18, 2)
            .IsRequired(false);

        builder.HasAlternateKey(o => new { o.Id, o.ClienteId, o.InstrumentoId })
            .HasName("ux_operacoes_id_cliente_instrumento");

        builder.HasOne<Operacao>()
            .WithMany()
            .HasPrincipalKey(o => new { o.Id, o.ClienteId, o.InstrumentoId })
            .HasForeignKey(o => new { o.EstornaOperacaoId, o.ClienteId, o.InstrumentoId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_operacoes_operacoes_estorna_operacao_id");

        builder.HasIndex(o => new { o.ClienteId, o.DataEvento })
            .IsDescending(false, true)
            .HasDatabaseName("ix_operacoes_cliente");

        builder.HasIndex(o => o.EstornaOperacaoId)
            .IsUnique()
            .HasFilter("estorna_operacao_id IS NOT NULL")
            .HasDatabaseName("ix_operacoes_estorna_unico");

        builder.HasIndex(o => new { o.EstornaOperacaoId, o.ClienteId, o.InstrumentoId })
            .HasDatabaseName("ix_operacoes_estorna_cliente_instrumento");

        builder.HasIndex(o => new { o.ClienteId, o.InstrumentoId })
            .HasDatabaseName("ix_operacoes_cliente_instrumento");
    }
}
