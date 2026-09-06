using Operacoes.Domain.Operacoes;

namespace Operacoes.Domain.Tests.Operacoes;

public sealed class OperacaoTests
{
    private static readonly TipoOperacao TipoValido = TipoOperacao.Aplicacao;
    private static readonly DateOnly DataEventoValida = new(2026, 1, 1);
    private static readonly DateTimeOffset RegistradoEmValido = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
            registradoEm: RegistradoEmValido);

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
            registradoEm: RegistradoEmValido);

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
            registradoEm: RegistradoEmValido);

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
            registradoEm: RegistradoEmValido);

        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoIdVazio, result.Error);
    }

    // --- Defeito A (revisor): mesmos invariantes dos CHECKs ck_operacoes_estorno_nao_auto e
    // ck_operacoes_estorno_coerente, agora também no Domain — sem isso, o erro previsível de
    // cliente vira DbUpdateException (500) em vez de 4xx no F3. ---

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
            estornaOperacaoId: "op-1");

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
            registradoEm: RegistradoEmValido);

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
            estornaOperacaoId: "op-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("op-1", result.Value.EstornaOperacaoId);
    }

    // --- Segunda revisão adversarial: null significa "sem referência"; string vazia ou só espaços
    // é entrada malformada, não sinônimo de null — Domínio e banco tinham veredito diferente para
    // esse caso (Create devolvia sucesso, o INSERT correspondente violava ck_operacoes_estorno_coerente).
    // A prova de que os dois concordam de fato está em SchemaTests.DominioEBanco_ConcordamSobreEstornaOperacaoId
    // (Operacoes.API.Tests), contra um Postgres real; aqui só o lado do Domínio. ---

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
            estornaOperacaoId: estornaOperacaoId);

        // EstornoReferenciaVazia, não EstornoIncoerente: string em branco é malformada
        // independentemente do tipo — não é o mesmo defeito de "referência preenchida fora de um
        // estorno" (esse é o caso de "op-1" preenchido, coberto acima).
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
            estornaOperacaoId: " op-1 ");

        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoAutoReferente, result.Error);
    }

    // --- Terceira auditoria de conformidade: normalização aplicada a um campo só (estornaOperacaoId)
    // enquanto id, clienteId e instrumentoId — gravados na mesma tabela append-only — continuavam só
    // validados, sem normalizar. Mesmo defeito do LEIA-ME-KIT ("Normalizar de um lado só"): "op-1" e
    // " op-1 " virariam duas linhas distintas e permanentes. Os três passam a trimar, no mesmo padrão
    // já aplicado ao EstornaOperacaoId — validar primeiro, normalizar depois, gravar o normalizado. ---

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
            registradoEm: RegistradoEmValido);

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
            registradoEm: RegistradoEmValido);

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
            registradoEm: RegistradoEmValido);

        Assert.True(result.IsSuccess);
        Assert.Equal("td:tesouro-selic-2029-03-01", result.Value.InstrumentoId);
    }

    // Caixa preservada de propósito: `Create` normaliza só ruído de transporte (Trim), nunca
    // transforma o valor. Canonizar identidade do Hub é responsabilidade do Hub (§3 do PADROES,
    // ADR-12) — o molde Hub.Domain.Instrumentos.InstrumentoId baixa caixa porque a política é dele.
    // O valor abaixo tem maiúscula não porque o Hub emita assim (não emite: ele faz
    // Trim().ToLowerInvariant() incondicionalmente), mas para provar que ESTE código não mexe na
    // caixa do que recebe, venha de onde vier.
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
            registradoEm: RegistradoEmValido);

        Assert.True(result.IsSuccess);
        Assert.Equal("acao:PETR4", result.Value.InstrumentoId);
    }

    // Id trimado precisa continuar participando da comparação de auto-referência — " op-1 " como id
    // e "op-1" como estornaOperacaoId são a MESMA operação normalizada.
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
            estornaOperacaoId: "op-1");

        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoAutoReferente, result.Error);
    }
}
