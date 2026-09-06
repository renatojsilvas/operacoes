using System.Reflection;
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
public sealed class AppDbContextNumericOverflowNetTests
{
    private readonly ApiTestFactory _factory;

    public AppDbContextNumericOverflowNetTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private static readonly ConstructorInfo OperacaoConstructor = typeof(Operacao).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance,
        binder: null,
        types:
        [
            typeof(string), typeof(string), typeof(string), typeof(TipoOperacao), typeof(decimal),
            typeof(decimal), typeof(DateOnly), typeof(DateTimeOffset), typeof(string),
        ],
        modifiers: null)!;

    private static Operacao NovaOperacaoForaDaPrecisaoDaColuna(
        string id, string clienteId, string instrumentoId, decimal quantidade, decimal valorFinanceiro) =>
        (Operacao)OperacaoConstructor.Invoke(
        [
            id, clienteId, instrumentoId, TipoOperacao.Aporte, quantidade, valorFinanceiro,
            new DateOnly(2020, 1, 1), DateTimeOffset.UtcNow, null,
        ]);

    [Fact]
    public async Task SaveChanges_ComQuantidadeAlemDaMagnitudeDaColuna_DevolveResultFailureUnprocessableNaoLancaExcecao()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = (IUnitOfWork)db;

        var operacao = NovaOperacaoForaDaPrecisaoDaColuna(
            id: $"op-overflow-{Guid.NewGuid():N}",
            clienteId: "cliente-overflow-net",
            instrumentoId: "td:tesouro-selic-2029",
            quantidade: 12345678901.1m,
            valorFinanceiro: 1000m);

        db.Operacoes.Add(operacao);

        var resultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unprocessable, resultado.Error.Type);

        Assert.Empty(db.ChangeTracker.Entries());

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = operacao.Id });
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task SaveChanges_ComValorFinanceiroAlemDaMagnitudeDaColuna_DevolveResultFailureUnprocessableNaoLancaExcecao()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = (IUnitOfWork)db;

        var operacao = NovaOperacaoForaDaPrecisaoDaColuna(
            id: $"op-overflow-{Guid.NewGuid():N}",
            clienteId: "cliente-overflow-net-2",
            instrumentoId: "td:tesouro-selic-2029",
            quantidade: 10m,
            valorFinanceiro: 12345678901234567.1m);

        db.Operacoes.Add(operacao);

        var resultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(ErrorType.Unprocessable, resultado.Error.Type);

        Assert.Empty(db.ChangeTracker.Entries());

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id = operacao.Id });
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task SaveChanges_ComOperacaoValida_DepoisDeUmaFalhaDeOverflowNaMesmaInstancia_AindaConsegueGravar()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = (IUnitOfWork)db;

        var operacaoInvalida = NovaOperacaoForaDaPrecisaoDaColuna(
            id: $"op-overflow-{Guid.NewGuid():N}",
            clienteId: "cliente-overflow-net-3",
            instrumentoId: "td:tesouro-selic-2029",
            quantidade: 12345678901.1m,
            valorFinanceiro: 1000m);
        db.Operacoes.Add(operacaoInvalida);
        var falhaResultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.True(falhaResultado.IsFailure);

        var operacaoValida = Operacao.Create(
            id: $"op-overflow-ok-{Guid.NewGuid():N}",
            clienteId: "cliente-overflow-net-3",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: new DateOnly(2020, 1, 1),
            registradoEm: DateTimeOffset.UtcNow,
            hoje: new DateOnly(2020, 1, 1)).Value;
        db.Operacoes.Add(operacaoValida);
        var sucessoResultado = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(sucessoResultado.IsSuccess);
    }
}
