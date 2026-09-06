using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Operacoes.Domain.Outbox;
using Operacoes.Infrastructure.Persistence;
using Operacoes.Infrastructure.Persistence.Repositories;

namespace Operacoes.API.Tests.Integration;

[Collection("outbox-postgres")]
public sealed class OutboxReadRepositoryIntegrationTests(OutboxPostgresFixture fixture)
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static async Task<OutboxMessage> CriarMensagemAsync(
        AppDbContext db, string tipo, string routingKey, string payload, DateTimeOffset criadoEm)
    {
        var mensagem = OutboxMessage.Create(tipo, routingKey, payload, criadoEm).Value;
        db.OutboxMessages.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem;
    }

    [Fact]
    public async Task ObterPendentesAsync_OrdenaPorIdCrescenteERespeitaOLimite()
    {
        await fixture.LimparAsync();

        await using (var db = new AppDbContext(fixture.Options))
        {
            for (var i = 0; i < 5; i++)
            {
                await CriarMensagemAsync(db, "TradeRegistered", $"trades.registered.{i}", "{}", Agora);
            }
        }

        var repo = new OutboxReadRepository(fixture.DataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(3, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.Count);
        Assert.Equal(resultado.Value.Select(m => m.Id).OrderBy(id => id), resultado.Value.Select(m => m.Id));
        Assert.Equal(
            ["trades.registered.0", "trades.registered.1", "trades.registered.2"],
            resultado.Value.Select(m => m.RoutingKey));
    }

    [Fact]
    public async Task ObterPendentesAsync_ExcluiMensagensJaPublicadas()
    {
        await fixture.LimparAsync();

        await using (var db = new AppDbContext(fixture.Options))
        {
            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.pendente-a", "{}", Agora);

            var publicada = await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.publicada", "{}", Agora);
            publicada.MarcarPublicado(Agora.AddMinutes(1));
            await db.SaveChangesAsync();

            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.pendente-b", "{}", Agora);
        }

        var repo = new OutboxReadRepository(fixture.DataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(10, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.DoesNotContain(resultado.Value, m => m.RoutingKey == "trades.registered.publicada");
        Assert.Contains(resultado.Value, m => m.RoutingKey == "trades.registered.pendente-a");
        Assert.Contains(resultado.Value, m => m.RoutingKey == "trades.registered.pendente-b");
    }

    [Fact]
    public async Task ObterPendentesAsync_PayloadChegaComoStringJsonIntegra()
    {
        await fixture.LimparAsync();

        const string payloadOriginal =
            """{"v":1,"tipo":"TradeRegistered","instrumentoId":"td:tesouro-selic-2029","valorFinanceiro":"1234.56","texto":"acentuação, aspas \" e emoji 🎯"}""";

        await using (var db = new AppDbContext(fixture.Options))
        {
            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.payload", payloadOriginal, Agora);
        }

        var repo = new OutboxReadRepository(fixture.DataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(10, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var mensagem = Assert.Single(resultado.Value);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(payloadOriginal), JsonNode.Parse(mensagem.Payload)),
            $"Payload divergiu após ida e volta pelo Postgres. Original: {payloadOriginal}; Obtido: {mensagem.Payload}");
    }

    [Fact]
    public async Task ObterBacklogAsync_ContaPendentesEDevolveIdadeDaMaisAntiga()
    {
        await fixture.LimparAsync();

        await using (var db = new AppDbContext(fixture.Options))
        {
            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.a", "{}", Agora.AddMinutes(-10));
            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.b", "{}", Agora.AddMinutes(-5));
            await CriarMensagemAsync(db, "TradeRegistered", "trades.registered.c", "{}", Agora.AddMinutes(-1));

            var publicadaAntiga = await CriarMensagemAsync(
                db, "TradeRegistered", "trades.registered.publicada-antiga", "{}", Agora.AddMinutes(-60));
            publicadaAntiga.MarcarPublicado(Agora.AddMinutes(-59));
            await db.SaveChangesAsync();
        }

        var timeProvider = new FakeTimeProvider(Agora);
        var repo = new OutboxReadRepository(fixture.DataSource, timeProvider, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterBacklogAsync(CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.Pendentes);
        Assert.Equal(TimeSpan.FromMinutes(10), resultado.Value.IdadeMaisAntiga);
    }

    [Fact]
    public async Task ObterBacklogAsync_SemMensagensPendentes_DevolveZeroEIdadeNula()
    {
        await fixture.LimparAsync();

        var repo = new OutboxReadRepository(fixture.DataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterBacklogAsync(CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(0, resultado.Value.Pendentes);
        Assert.Null(resultado.Value.IdadeMaisAntiga);
    }
}
