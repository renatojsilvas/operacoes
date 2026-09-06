using Dapper;
using Npgsql;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;

namespace Operacoes.Infrastructure.Persistence.Repositories;

public sealed class OperacaoReadRepository(NpgsqlDataSource dataSource) : IOperacaoReadRepository
{
    static OperacaoReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    private const string SqlExisteComoReferenciaDeEstorno =
        """
        SELECT EXISTS (
            SELECT 1 FROM operacoes
            WHERE id = @id AND cliente_id = @clienteId AND instrumento_id = @instrumentoId
        )
        """;

    private const string SqlObterPorId =
        """
        SELECT
            id AS "Id",
            cliente_id AS "ClienteId",
            instrumento_id AS "InstrumentoId",
            operacao AS "Operacao",
            quantidade AS "Quantidade",
            valor_financeiro AS "ValorFinanceiro",
            data_evento AS "DataEvento",
            registrado_em AS "RegistradoEm",
            estorna_operacao_id AS "EstornaOperacaoId"
        FROM operacoes
        WHERE id = @id
        """;

    public async Task<Result<bool>> ExisteComoReferenciaDeEstornoAsync(
        string estornaOperacaoId, string clienteId, string instrumentoId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var existe = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                SqlExisteComoReferenciaDeEstorno,
                new { id = estornaOperacaoId, clienteId, instrumentoId },
                cancellationToken: ct));

        return Result<bool>.Success(existe);
    }

    public async Task<Result<OperacaoConsulta>> ObterPorIdAsync(string id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var linha = await connection.QuerySingleOrDefaultAsync<OperacaoRegistradaRow>(
            new CommandDefinition(SqlObterPorId, new { id }, cancellationToken: ct));

        return Result<OperacaoConsulta>.Success(
            linha is null ? OperacaoConsulta.NaoEncontrada : OperacaoConsulta.DeLinha(linha));
    }
}
