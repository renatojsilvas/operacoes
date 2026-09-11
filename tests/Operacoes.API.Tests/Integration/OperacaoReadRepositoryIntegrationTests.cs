using Operacoes.Domain.Operacoes;
using Operacoes.Infrastructure.Persistence;
using Operacoes.Infrastructure.Persistence.Repositories;

namespace Operacoes.API.Tests.Integration;

[Collection("outbox-postgres")]
public sealed class OperacaoReadRepositoryIntegrationTests(OutboxPostgresFixture fixture)
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Agora.UtcDateTime);

    private static async Task CriarOperacaoAsync(
        AppDbContext db, string id, string clienteId, string instrumentoId)
    {
        var operacao = Operacao.Create(
            id: id,
            clienteId: clienteId,
            instrumentoId: instrumentoId,
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: Hoje,
            registradoEm: Agora,
            hoje: Hoje,
            valorOrigemSaldo: 500m).Value;

        db.Operacoes.Add(operacao);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ObterInstrumentosNegociadosAsync_ComClienteSemOperacoes_DevolveListaVaziaNaoNula()
    {
        await fixture.LimparAsync();

        var repo = new OperacaoReadRepository(fixture.DataSource);

        var resultado = await repo.ObterInstrumentosNegociadosAsync("cliente-sem-operacoes", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.NotNull(resultado.Value);
        Assert.Empty(resultado.Value);
    }

    [Fact]
    public async Task ObterInstrumentosNegociadosAsync_ComMesmoInstrumentoNegociadoDuasVezes_DevolveApenasUmaOcorrencia()
    {
        await fixture.LimparAsync();

        await using (var db = new AppDbContext(fixture.Options))
        {
            await CriarOperacaoAsync(db, "op-negociado-1", "cliente-1", "td:tesouro-selic-2029");
            await CriarOperacaoAsync(db, "op-negociado-2", "cliente-1", "td:tesouro-selic-2029");
        }

        var repo = new OperacaoReadRepository(fixture.DataSource);

        var resultado = await repo.ObterInstrumentosNegociadosAsync("cliente-1", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(["td:tesouro-selic-2029"], resultado.Value);
    }

    [Fact]
    public async Task ObterInstrumentosNegociadosAsync_ComVariosInstrumentos_DevolveTodosSemDuplicarERespeitaOCliente()
    {
        await fixture.LimparAsync();

        await using (var db = new AppDbContext(fixture.Options))
        {
            await CriarOperacaoAsync(db, "op-1", "cliente-1", "td:tesouro-selic-2029");
            await CriarOperacaoAsync(db, "op-2", "cliente-1", "td:tesouro-ipca-2035");
            await CriarOperacaoAsync(db, "op-3", "cliente-2", "td:tesouro-prefixado-2031");
        }

        var repo = new OperacaoReadRepository(fixture.DataSource);

        var resultado = await repo.ObterInstrumentosNegociadosAsync("cliente-1", CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.Count);
        Assert.Contains("td:tesouro-selic-2029", resultado.Value);
        Assert.Contains("td:tesouro-ipca-2035", resultado.Value);
        Assert.DoesNotContain("td:tesouro-prefixado-2031", resultado.Value);
    }
}
