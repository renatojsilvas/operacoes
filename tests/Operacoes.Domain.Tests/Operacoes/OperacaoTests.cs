using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Domain.Tests.Operacoes;

public sealed class OperacaoTests
{
    private static readonly TipoOperacao TipoValido = TipoOperacao.Aplicacao;
    private static readonly DateOnly DataEventoValida = new(2026, 1, 1);
    private static readonly DateTimeOffset RegistradoEmValido = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly HojeValido = new(2026, 1, 1);
    [Fact]
    public void Create_ComDadosValidos_DevePreencherTodasAsPropriedades()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        var operacao = result.Value;
        Assert.Equal("op-1", operacao.Id);
        Assert.Equal("cliente-1", operacao.ClienteId);
        Assert.Equal("td:tesouro-selic-2029-03-01", operacao.InstrumentoId);
        Assert.Equal(TipoValido, operacao.Tipo);
        Assert.Equal(10m, operacao.Quantidade);
        Assert.Equal(1000m, operacao.ValorFinanceiro);
        Assert.Equal(DataEventoValida, operacao.DataEvento);
        Assert.Equal(RegistradoEmValido, operacao.RegistradoEm);
        Assert.Null(operacao.EstornaOperacaoId);
    }

    [Fact]
    public void Create_ComEstornaOperacaoId_DevePreencheLo()
    {
        var result = Operacao.Create(
            id: "op-2",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1");
        Assert.True(result.IsSuccess);
        Assert.Equal("op-1", result.Value.EstornaOperacaoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComIdVazio_DeveFalhar(string? id)
    {
        var result = Operacao.Create(
            id: id!,
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.IdVazio, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComClienteIdVazio_DeveFalhar(string? clienteId)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: clienteId!,
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ClienteIdVazio, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComInstrumentoIdVazio_DeveFalhar(string? instrumentoId)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: instrumentoId!,
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoIdVazio, result.Error);
    }

    [Fact]
    public void Create_ComEstornaOperacaoIdIgualAoProprioId_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1");
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoAutoReferente, result.Error);
    }

    [Fact]
    public void Create_ComTipoDiferenteDeEstornoEEstornaOperacaoIdPreenchido_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-2",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1",
            valorOrigemSaldo: 500m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComTipoEstornoSemEstornaOperacaoId_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-3",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComEstornoValidoReferenciandoOutraOperacao_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-4",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1");
        Assert.True(result.IsSuccess);
        Assert.Equal("op-1", result.Value.EstornaOperacaoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComEstornaOperacaoIdVazioOuSoEspacos_TipoEstorno_DeveFalhar(string estornaOperacaoId)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: estornaOperacaoId);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoReferenciaVazia, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComEstornaOperacaoIdVazioOuSoEspacos_TipoNaoEstorno_DeveFalhar(string estornaOperacaoId)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: estornaOperacaoId,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoReferenciaVazia, result.Error);
    }

    [Fact]
    public void Create_ComEstornaOperacaoIdComEspacosAoRedor_DeveSerAceitoETrimado()
    {
        var result = Operacao.Create(
            id: "op-2",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: " op-1 ");
        Assert.True(result.IsSuccess);
        Assert.Equal("op-1", result.Value.EstornaOperacaoId);
    }

    [Fact]
    public void Create_ComEstornaOperacaoIdIgualAoProprioIdComEspacosAoRedor_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: " op-1 ");
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoAutoReferente, result.Error);
    }

    [Fact]
    public void Create_ComIdComEspacosAoRedor_DeveSerAceitoETrimado()
    {
        var result = Operacao.Create(
            id: " op-1 ",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal("op-1", result.Value.Id);
    }

    [Fact]
    public void Create_ComClienteIdComEspacosAoRedor_DeveSerAceitoETrimado()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: " cliente-1 ",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal("cliente-1", result.Value.ClienteId);
    }

    [Fact]
    public void Create_ComInstrumentoIdComEspacosAoRedor_DeveSerAceitoETrimado()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: " td:tesouro-selic-2029-03-01 ",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal("td:tesouro-selic-2029-03-01", result.Value.InstrumentoId);
    }

    [Fact]
    public void Create_ComInstrumentoIdComMaiuscula_NaoDeveBaixarCaixa()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "acao:PETR4",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal("acao:PETR4", result.Value.InstrumentoId);
    }

    [Fact]
    public void Create_ComIdComEspacosAoRedorIgualAoEstornaOperacaoIdNormalizado_DeveFalhar()
    {
        var result = Operacao.Create(
            id: " op-1 ",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1");
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoAutoReferente, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.00000001)]
    [InlineData(-10)]
    public void Create_ComQuantidadeMenorOuIgualAZero_DeveFalhar(decimal quantidade)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: quantidade,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.QuantidadeInvalida, result.Error);
        Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-1000)]
    public void Create_ComValorFinanceiroMenorOuIgualAZero_DeveFalhar(decimal valorFinanceiro)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: valorFinanceiro,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorFinanceiroInvalido, result.Error);
        Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
    }

    [Theory]
    [InlineData(12345678901.1)]
    [InlineData(10000000000)]
    public void Create_ComQuantidadeComOnzeOuMaisDigitosInteiros_DeveFalhar(decimal quantidade)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: quantidade,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.QuantidadeExcedePrecisaoSuportada, result.Error);
        Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
    }

    [Fact]
    public void Create_ComQuantidadeComNoveCasasDecimais_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 1.123456789m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.QuantidadeExcedePrecisaoSuportada, result.Error);
    }

    [Fact]
    public void Create_ComQuantidadeNoLimiteMaximoSuportado_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 9999999999.99999999m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal(9999999999.99999999m, result.Value.Quantidade);
    }

    [Theory]
    [InlineData(12345678901234567.1)]
    [InlineData(10000000000000000)]
    public void Create_ComValorFinanceiroComDezesseteOuMaisDigitosInteiros_DeveFalhar(decimal valorFinanceiro)
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: valorFinanceiro,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorFinanceiroExcedePrecisaoSuportada, result.Error);
        Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
    }

    [Fact]
    public void Create_ComValorFinanceiroComTresCasasDecimais_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000.123m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorFinanceiroExcedePrecisaoSuportada, result.Error);
    }

    [Fact]
    public void Create_ComValorFinanceiroNoLimiteMaximoSuportado_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 9999999999999999.99m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
        Assert.Equal(9999999999999999.99m, result.Value.ValorFinanceiro);
    }

    [Fact]
    public void Create_ComDataEventoPosteriorAHoje_DeveFalhar()
    {
        var hoje = new DateOnly(2026, 1, 1);
        var amanha = hoje.AddDays(1);
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: amanha,
            registradoEm: RegistradoEmValido,
            hoje: hoje,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.DataEventoFutura, result.Error);
        Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
    }

    [Fact]
    public void Create_ComDataEventoIgualAHoje_DeveSerAceito()
    {
        var hoje = new DateOnly(2026, 1, 1);
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: hoje,
            registradoEm: RegistradoEmValido,
            hoje: hoje,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_ComDataEventoNoPassado_DeveSerAceito()
    {
        var hoje = new DateOnly(2026, 1, 10);
        var ontem = hoje.AddDays(-9);
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoValido,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: ontem,
            registradoEm: RegistradoEmValido,
            hoje: hoje,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoAusente_TipoAplicacao_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aplicacao,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoAusente_TipoAporte_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoPresente_TipoResgate_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Resgate,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoPresente_TipoEstorno_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-2",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Estorno,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            estornaOperacaoId: "op-1",
            valorOrigemSaldo: 500m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoIncoerente, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoNegativo_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: -0.01m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoInvalido, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoMaiorQueValorFinanceiro_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 1000.01m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoExcedeValorFinanceiro, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoIgualAZero_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 0m);
        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.ValorOrigemSaldo);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoIgualAoValorFinanceiro_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 1000m);
        Assert.True(result.IsSuccess);
        Assert.Equal(1000m, result.Value.ValorOrigemSaldo);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoEntreZeroEValorFinanceiro_DeveSerAceito()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aplicacao,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 900m);
        Assert.True(result.IsSuccess);
        Assert.Equal(900m, result.Value.ValorOrigemSaldo);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoComTresCasasDecimais_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 500.123m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoExcedePrecisaoSuportada, result.Error);
    }

    [Fact]
    public void Create_ComValorOrigemSaldoComDezesseteOuMaisDigitosInteiros_DeveFalhar()
    {
        var result = Operacao.Create(
            id: "op-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029-03-01",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 9999999999999999.99m,
            dataEvento: DataEventoValida,
            registradoEm: RegistradoEmValido,
            hoje: HojeValido,
            valorOrigemSaldo: 12345678901234567.1m);
        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.ValorOrigemSaldoExcedePrecisaoSuportada, result.Error);
    }
}
