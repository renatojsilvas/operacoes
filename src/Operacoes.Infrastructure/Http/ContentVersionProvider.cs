using System.Globalization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Operacoes.Infrastructure.Persistence;

namespace Operacoes.Infrastructure.Http;

public sealed class ContentVersionProvider(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    IConfiguration configuration) : IContentVersionProvider
{
    private const string FormatoInstante = "O";
    private const string CatalogoTtlConfigKey = "Caching:CatalogoInstrumentos";
    private static readonly TimeSpan DefaultCatalogoTtl = TimeSpan.FromSeconds(60);

    private const string Sql =
        """
        SELECT
            (SELECT max(registrado_em) FROM operacoes) AS MaxRegistradoEm,
            (SELECT count(*)           FROM operacoes) AS OperacoesCount
        """;

    static ContentVersionProvider()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<ContentVersionRow>(
            new CommandDefinition(Sql, cancellationToken: cancellationToken));

        var maxRegistradoEm = row.MaxRegistradoEm?.ToString(FormatoInstante, CultureInfo.InvariantCulture) ?? "none";
        var balde = GetCatalogoBalde();

        return $"{maxRegistradoEm}-{row.OperacoesCount}-{balde}";
    }

    private long GetCatalogoBalde()
    {
        var configurado = configuration.GetValue<TimeSpan?>(CatalogoTtlConfigKey);

        if (configurado is { Ticks: <= 0 })
        {
            return timeProvider.GetUtcNow().Ticks;
        }

        var ttl = configurado is { Ticks: > 0 } valor ? valor : DefaultCatalogoTtl;

        return timeProvider.GetUtcNow().Ticks / ttl.Ticks;
    }

    private sealed record ContentVersionRow(DateTimeOffset? MaxRegistradoEm, long OperacoesCount);
}
