using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Operacoes.Application.Common.Interfaces;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class AppDbContextForeignKeyNetTests
{
    private readonly ApiTestFactory _factory;

    public AppDbContextForeignKeyNetTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private static Operacao NovaOperacao(
        string id, string clienteId, string instrumentoId, string? estornaOperacaoId = null,
        TipoOperacao? tipo = null) =>
        Operacao.Create(
            id: id,
            clienteId: clienteId,
            instrumentoId: instrumentoId,
            tipo: tipo ?? (estornaOperacaoId is null ? TipoOperacao.Aporte : TipoOperacao.Estorno),
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: new DateOnly(2020, 1, 1),
            registradoEm: DateTimeOffset.UtcNow,
            hoje: new DateOnly(2020, 1, 1),
            estornaOperacaoId: estornaOperacaoId).Value;

    [Fact]
    public async Task SaveChanges_ComEstornoReferenciandoOperacaoInexistente_DevolveResultFailureEstornoReferenciaInvalida()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = (IUnitOfWork)db;

        var estorno = NovaOperacao(
            id: $"op-fk-{Guid.NewGuid():N}",
            clienteId: "cliente-fk-net",
            instrumentoId: "td:tesouro-selic-2029",
            estornaOperacaoId: $"op-nao-existe-{Guid.NewGuid():N}");

        db.Operacoes.Add(estorno);

        var resultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoReferenciaInvalida, resultado.Error);
        Assert.Equal(ErrorType.Unprocessable, resultado.Error.Type);

        Assert.Empty(db.ChangeTracker.Entries());

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = estorno.Id });
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task SaveChanges_ComOperacaoValida_DepoisDeUmaFalhaDeFkNaMesmaInstancia_AindaConsegueGravar()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = (IUnitOfWork)db;

        var estornoInvalido = NovaOperacao(
            id: $"op-fk-{Guid.NewGuid():N}",
            clienteId: "cliente-fk-net-2",
            instrumentoId: "td:tesouro-selic-2029",
            estornaOperacaoId: $"op-nao-existe-{Guid.NewGuid():N}");
        db.Operacoes.Add(estornoInvalido);
        var falhaResultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.True(falhaResultado.IsFailure);

        var operacaoValida = NovaOperacao(
            id: $"op-fk-ok-{Guid.NewGuid():N}", clienteId: "cliente-fk-net-2", instrumentoId: "td:tesouro-selic-2029");
        db.Operacoes.Add(operacaoValida);
        var sucessoResultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(sucessoResultado.IsSuccess);
    }
}
