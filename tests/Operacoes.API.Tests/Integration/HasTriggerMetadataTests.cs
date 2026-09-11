using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operacoes.Domain.Operacoes;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

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
            registradoEm: default,
            hoje: new DateOnly(2026, 1, 1),
            valorOrigemSaldo: 500m).Value;
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
