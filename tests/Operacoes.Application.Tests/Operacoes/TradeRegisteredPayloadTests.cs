using System.Text.Json;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Application.Tests.Operacoes;

public sealed class TradeRegisteredPayloadTests
{
    private static readonly DateOnly DataEventoValida = new(2026, 6, 15);
    private static readonly DateTimeOffset RegistradoEmValido = new(2026, 6, 15, 14, 2, 11, TimeSpan.Zero);
    private static readonly DateOnly HojeValido = new(2026, 6, 15);

    [Theory]
    [InlineData("aporte")]
    [InlineData("aplicacao")]
    public void Serializar_ComTipoAporteOuAplicacao_IncluiValorOrigemSaldoComDuasDecimais(string tipoNome)
    {
        var tipo = TipoOperacao.FromName(tipoNome).Value;
        var operacao = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: tipo,
            quantidade: 2.86m,
            valorFinanceiro: 10000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 900m).Value;

        var payload = TradeRegisteredPayload.Serializar(operacao);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal("900.00", document.RootElement.GetProperty("valorOrigemSaldo").GetString());
    }

    [Fact]
    public void Serializar_ComTipoResgate_OmiteValorOrigemSaldo()
    {
        var operacao = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: TipoOperacao.Resgate,
            quantidade: 2.86m,
            valorFinanceiro: 10000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido).Value;

        var payload = TradeRegisteredPayload.Serializar(operacao);

        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.TryGetProperty("valorOrigemSaldo", out _));
        Assert.DoesNotContain("valorOrigemSaldo", payload);
    }

    [Fact]
    public void Serializar_ComTipoEstorno_OmiteValorOrigemSaldo()
    {
        var operacao = Operacao.Create(
            id: "op-2",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: TipoOperacao.Estorno,
            quantidade: 2.86m,
            valorFinanceiro: 10000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1").Value;

        var payload = TradeRegisteredPayload.Serializar(operacao);

        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.TryGetProperty("valorOrigemSaldo", out _));
        Assert.DoesNotContain("valorOrigemSaldo", payload);
    }

    [Fact]
    public void Serializar_ComValorOrigemSaldoMisto_ProduzOJsonEsperadoPorCompleto()
    {
        var operacao = Operacao.Create(
            id: "op-7f3a",
            clienteId: "cli-001",
            instrumentoId: "td:tesouro-ipca-2035-05-15",
            tipo: TipoOperacao.Aplicacao,
            quantidade: 2.86m,
            valorFinanceiro: 10000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 900m).Value;

        var payload = TradeRegisteredPayload.Serializar(operacao);

        var esperado = "{\"v\":1,\"tipo\":\"TradeRegistered\",\"tradeId\":\"op-7f3a\",\"clienteId\":\"cli-001\"," +
            "\"instrumentoId\":\"td:tesouro-ipca-2035-05-15\",\"operacao\":\"aplicacao\"," +
            "\"quantidade\":\"2.86000000\",\"valorFinanceiro\":\"10000.00\",\"dataEvento\":\"2026-06-15\"," +
            "\"registradoEm\":\"2026-06-15T14:02:11Z\",\"valorOrigemSaldo\":\"900.00\"}";

        Assert.Equal(esperado, payload);
    }
}
