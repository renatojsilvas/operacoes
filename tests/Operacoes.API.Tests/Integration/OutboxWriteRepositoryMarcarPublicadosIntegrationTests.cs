using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Operacoes.Domain.Outbox;
using Operacoes.Infrastructure.Persistence;
using Operacoes.Infrastructure.Persistence.Repositories;

namespace Operacoes.API.Tests.Integration;

[Collection("outbox-postgres")]
public sealed class OutboxWriteRepositoryMarcarPublicadosIntegrationTests(OutboxPostgresFixture fixture)
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static async Task<OutboxMessage> CriarMensagemAsync(AppDbContext db, string routingKey)
    {
        var mensagem = OutboxMessage.Create("TradeRegistered", routingKey, "{}", Agora).Value;
        db.OutboxMessages.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem;
    }

    [Fact]
    public async Task MarcarPublicadosAsync_MarcaAsLinhasEDevolveAContagemDeAfetados()
    {
        await fixture.LimparAsync();

        List<long> ids;
        await using (var db = new AppDbContext(fixture.Options))
        {
            var a = await CriarMensagemAsync(db, "trades.registered.marcar-a");
            var b = await CriarMensagemAsync(db, "trades.registered.marcar-b");
            ids = [a.Id, b.Id];
        }

        await using (var db = new AppDbContext(fixture.Options))
        {
            var repo = new OutboxWriteRepository(db, NullLogger<OutboxWriteRepository>.Instance);

            var resultado = await repo.MarcarPublicadosAsync(ids, Agora.AddMinutes(1), CancellationToken.None);

            Assert.True(resultado.IsSuccess);
            Assert.Equal(2, resultado.Value);
        }

        await using (var verificacao = new AppDbContext(fixture.Options))
        {
            var publicadas = await verificacao.OutboxMessages
                .Where(m => ids.Contains(m.Id))
                .ToListAsync();

            Assert.All(publicadas, m => Assert.Equal(Agora.AddMinutes(1), m.PublicadoEm));
        }
    }

    [Fact]
    public async Task MarcarPublicadosAsync_RemarcarLinhaJaPublicada_EhIdempotenteEAfetaZero()
    {
        await fixture.LimparAsync();

        long id;
        await using (var db = new AppDbContext(fixture.Options))
        {
            var mensagem = await CriarMensagemAsync(db, "trades.registered.idempotente");
            id = mensagem.Id;
        }

        await using (var db = new AppDbContext(fixture.Options))
        {
            var repo = new OutboxWriteRepository(db, NullLogger<OutboxWriteRepository>.Instance);

            var primeiraMarcacao = await repo.MarcarPublicadosAsync([id], Agora.AddMinutes(1), CancellationToken.None);
            Assert.True(primeiraMarcacao.IsSuccess);
            Assert.Equal(1, primeiraMarcacao.Value);

            var segundaMarcacao = await repo.MarcarPublicadosAsync([id], Agora.AddMinutes(2), CancellationToken.None);
            Assert.True(segundaMarcacao.IsSuccess);
            Assert.Equal(0, segundaMarcacao.Value);
        }

        await using (var verificacao = new AppDbContext(fixture.Options))
        {
            var mensagem = await verificacao.OutboxMessages.SingleAsync(m => m.Id == id);
            Assert.Equal(Agora.AddMinutes(1), mensagem.PublicadoEm);
        }
    }

    [Fact]
    public async Task MarcarPublicadosAsync_ListaDeIdsVazia_NaoAfetaNinguemEDevolveSucesso()
    {
        await fixture.LimparAsync();

        await using var db = new AppDbContext(fixture.Options);
        var repo = new OutboxWriteRepository(db, NullLogger<OutboxWriteRepository>.Instance);

        var resultado = await repo.MarcarPublicadosAsync([], Agora, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(0, resultado.Value);
    }
}
