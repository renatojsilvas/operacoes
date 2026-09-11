using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Application.Operacoes;

public static class TradeRegisteredPayload
{
    public const string Tipo = "TradeRegistered";
    public const string RoutingKey = "trades.registered";

    private const string DataFormat = "yyyy-MM-dd";
    private const string InstanteFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serializar(Operacao operacao)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        var contrato = new TradeRegisteredContrato(
            V: 1,
            Tipo: Tipo,
            TradeId: operacao.Id,
            ClienteId: operacao.ClienteId,
            InstrumentoId: operacao.InstrumentoId,
            Operacao: operacao.Tipo.Name,
            Quantidade: operacao.Quantidade.ToString("F8", CultureInfo.InvariantCulture),
            ValorFinanceiro: operacao.ValorFinanceiro.ToString("F2", CultureInfo.InvariantCulture),
            DataEvento: operacao.DataEvento.ToString(DataFormat, CultureInfo.InvariantCulture),
            RegistradoEm: operacao.RegistradoEm.UtcDateTime.ToString(InstanteFormat, CultureInfo.InvariantCulture),
            EstornaTradeId: operacao.EstornaOperacaoId,
            ValorOrigemSaldo: operacao.ValorOrigemSaldo?.ToString("F2", CultureInfo.InvariantCulture));

        return JsonSerializer.Serialize(contrato, SerializerOptions);
    }

    private sealed record TradeRegisteredContrato(
        int V,
        string Tipo,
        string TradeId,
        string ClienteId,
        string InstrumentoId,
        string Operacao,
        string Quantidade,
        string ValorFinanceiro,
        string DataEvento,
        string RegistradoEm,
        string? EstornaTradeId,
        string? ValorOrigemSaldo);
}
