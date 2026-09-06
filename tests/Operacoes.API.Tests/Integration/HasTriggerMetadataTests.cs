using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operacoes.Domain.Operacoes;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

// Terceira auditoria de conformidade, item 2: prova de que t.HasTrigger("trg_operacoes_imutavel")
// em OperacaoConfiguration não muda o comportamento do SaveChanges no Npgsql. Spike registrado —
// resultado verificado contra Postgres real: (a) a anotação é aceita sem erro; (b) `dotnet ef
// migrations script` gera SQL idêntico antes/depois (a migration de teste do spike ficou com Up/Down
// vazios); (c) este teste prova que o SaveChanges continua buscando o valor gerado pelo servidor
// (registrado_em DEFAULT now()) — se o Npgsql abandonasse a cláusula RETURNING por causa do metadado
// de trigger (como o SqlServer faz com OUTPUT, via SqlServerOutputClauseConvention — convenção que só
// existe no assembly do provider SqlServer, confirmado por inspeção; o assembly do Npgsql não tem
// nenhuma convenção que reaja ao metadado de trigger), esse valor viria default(DateTimeOffset)
// em vez do timestamp real.
[Collection("api")]
public sealed class HasTriggerMetadataTests
{
    private readonly ApiTestFactory _factory;

    public HasTriggerMetadataTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SaveChanges_ComHasTriggerDeclarado_AindaTrazDeVoltaORegistradoEmGeradoPeloServidor()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var operacao = Operacao.Create(
            id: $"op-hastrigger-{Guid.NewGuid():N}",
            clienteId: "cliente-hastrigger",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: new DateOnly(2026, 1, 1),
            registradoEm: default).Value;

        db.Operacoes.Add(operacao);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(default, operacao.RegistradoEm);

        var registradoEmNoBanco = await db.Database
            .SqlQueryRaw<DateTimeOffset>(
                "SELECT registrado_em AS \"Value\" FROM operacoes WHERE id = {0}", operacao.Id)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(registradoEmNoBanco, operacao.RegistradoEm);
    }
}
