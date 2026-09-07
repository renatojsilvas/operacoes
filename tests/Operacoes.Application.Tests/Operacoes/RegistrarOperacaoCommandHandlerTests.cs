using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Operacoes.Application.Catalogo;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Application.Tests.Operacoes;

public sealed class RegistrarOperacaoCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Agora);
    private readonly FakeOperacaoReadRepository _readRepository = new();
    private readonly FakeOperacaoWriteRepository _writeRepository = new();
    private readonly FakeOutboxWriteRepository _outboxRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RegistrarOperacaoCommandHandler CriarHandler(Result<bool> hubResultado) =>
        new(
            new FakeHubCatalogoClient(hubResultado),
            _readRepository,
            _writeRepository,
            _outboxRepository,
            _unitOfWork,
            _timeProvider,
            NullLogger<RegistrarOperacaoCommandHandler>.Instance);

    private static RegistrarOperacaoCommand ComandoValido(
        string clienteId = "cliente-1",
        string instrumentoId = "td:tesouro-selic-2029",
        string tipo = "aporte",
        decimal quantidade = 10m,
        decimal valorFinanceiro = 1000m,
        string? estornaOperacaoId = null,
        string idempotencyKey = "chave-1") =>
        new(clienteId, instrumentoId, tipo, quantidade, valorFinanceiro, new DateOnly(2026, 6, 15), estornaOperacaoId, idempotencyKey);

    [Fact]
    public async Task Handle_ComTipoInvalido_FalhaSemChamarHub()
    {
        var handler = CriarHandler(Result<bool>.Success(true));
        var comando = ComandoValido(tipo: "voo-espacial");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.TipoInvalido, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ComQuantidadeInvalida_FalhaAntesDeChamarHub()
    {
        var hub = new FakeHubCatalogoClient(Result<bool>.Success(true));
        var handler = new RegistrarOperacaoCommandHandler(
            hub, _readRepository, _writeRepository, _outboxRepository, _unitOfWork, _timeProvider,
            NullLogger<RegistrarOperacaoCommandHandler>.Instance);

        var comando = ComandoValido(quantidade: 0);

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.QuantidadeInvalida, resultado.Error);

        Assert.Empty(hub.Chamadas);
    }

    [Fact]
    public async Task Handle_QuandoHubFalha_DevolveErroDoHubSemGravarNada()
    {
        var erroHub = CatalogoErrors.HubIndisponivel;
        var handler = CriarHandler(Result<bool>.Failure(erroHub));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(erroHub, resultado.Error);
        Assert.Equal(ErrorType.Unavailable, resultado.Error.Type);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Empty(_outboxRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_QuandoHubDizQueInstrumentoNaoExiste_Devolve422SemGravarNada()
    {
        var handler = CriarHandler(Result<bool>.Success(false));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoInexistente, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_QuandoHubDevolveCatalogoNaoVazioComApenasPrefixoSemMatchExato_Devolve422SemGravarNada()
    {
        var hub = new FakeHubCatalogoClient(Result<bool>.Success(true))
        {
            Catalogo = [new InstrumentoCatalogo("td:x-2030", "titulo-publico", "Tesouro X 2030", Vencido: false)],
        };
        var handler = new RegistrarOperacaoCommandHandler(
            hub, _readRepository, _writeRepository, _outboxRepository, _unitOfWork, _timeProvider,
            NullLogger<RegistrarOperacaoCommandHandler>.Instance);

        var comando = ComandoValido(instrumentoId: "td:x");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoInexistente, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_QuandoHubDevolveCatalogoNaoVazioComApenasDivergenciaDeCaixa_Devolve422SemGravarNada()
    {
        var hub = new FakeHubCatalogoClient(Result<bool>.Success(true))
        {
            Catalogo = [new InstrumentoCatalogo("TD:X", "titulo-publico", "Tesouro X", Vencido: false)],
        };
        var handler = new RegistrarOperacaoCommandHandler(
            hub, _readRepository, _writeRepository, _outboxRepository, _unitOfWork, _timeProvider,
            NullLogger<RegistrarOperacaoCommandHandler>.Instance);

        var comando = ComandoValido(instrumentoId: "td:x");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoInexistente, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_QuandoHubDevolveCatalogoComNomeExibicaoIgualAoInstrumentoIdMasIdDiferente_Devolve422SemGravarNada()
    {
        var hub = new FakeHubCatalogoClient(Result<bool>.Success(true))
        {
            Catalogo = [new InstrumentoCatalogo("td:outro-instrumento", "titulo-publico", "td:tesouro-selic-2029", Vencido: false)],
        };
        var handler = new RegistrarOperacaoCommandHandler(
            hub, _readRepository, _writeRepository, _outboxRepository, _unitOfWork, _timeProvider,
            NullLogger<RegistrarOperacaoCommandHandler>.Instance);

        var comando = ComandoValido(instrumentoId: "td:tesouro-selic-2029");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.InstrumentoInexistente, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ComEstornoDeReferenciaInvalida_DevolveErroSemGravarNada()
    {
        _readRepository.ReferenciaDeEstornoValida = Result<bool>.Success(false);
        var handler = CriarHandler(Result<bool>.Success(true));

        var comando = ComandoValido(tipo: "estorno", estornaOperacaoId: "op-inexistente");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoReferenciaInvalida, resultado.Error);
        Assert.Empty(_writeRepository.Adicionadas);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
        var chamada = Assert.Single(_readRepository.ChamadasDeReferencia);
        Assert.Equal(("op-inexistente", "cliente-1", "td:tesouro-selic-2029"), chamada);
    }

    [Fact]
    public async Task Handle_ComOperacaoValida_GravaEDevolveReplayFalso()
    {
        var handler = CriarHandler(Result<bool>.Success(true));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.False(resultado.Value.Replay);
        Assert.Equal("cliente-1", resultado.Value.ClienteId);
        Assert.Equal("td:tesouro-selic-2029", resultado.Value.InstrumentoId);
        Assert.Equal("aporte", resultado.Value.Tipo);
        Assert.StartsWith("op-", resultado.Value.Id);

        Assert.Single(_writeRepository.Adicionadas);
        Assert.Single(_outboxRepository.Adicionadas);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);

        var outboxMsg = _outboxRepository.Adicionadas[0];
        Assert.Equal("TradeRegistered", outboxMsg.Tipo);
        Assert.Equal("trades.registered", outboxMsg.RoutingKey);
        Assert.Contains("\"tradeId\":\"" + resultado.Value.Id + "\"", outboxMsg.Payload);
        Assert.DoesNotContain("estornaTradeId", outboxMsg.Payload);
    }

    [Fact]
    public async Task Handle_ComEstornoValido_PayloadDoOutboxContemEstornaTradeId()
    {
        _readRepository.ReferenciaDeEstornoValida = Result<bool>.Success(true);
        var handler = CriarHandler(Result<bool>.Success(true));

        var comando = ComandoValido(tipo: "estorno", estornaOperacaoId: "op-original");

        var resultado = await handler.Handle(comando, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var outboxMsg = Assert.Single(_outboxRepository.Adicionadas);
        Assert.Contains("\"estornaTradeId\":\"op-original\"", outboxMsg.Payload);
    }

    [Fact]
    public async Task Handle_MesmoClienteIdEIdempotencyKey_DerivaOMesmoId()
    {
        var handler1 = CriarHandler(Result<bool>.Success(true));
        var resultado1 = await handler1.Handle(ComandoValido(), CancellationToken.None);

        var readRepo2 = new FakeOperacaoReadRepository();
        var writeRepo2 = new FakeOperacaoWriteRepository();
        var outboxRepo2 = new FakeOutboxWriteRepository();
        var unitOfWork2 = new FakeUnitOfWork();
        var handler2 = new RegistrarOperacaoCommandHandler(
            new FakeHubCatalogoClient(Result<bool>.Success(true)), readRepo2, writeRepo2, outboxRepo2, unitOfWork2,
            _timeProvider, NullLogger<RegistrarOperacaoCommandHandler>.Instance);

        var resultado2 = await handler2.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado1.IsSuccess);
        Assert.True(resultado2.IsSuccess);
        Assert.Equal(resultado1.Value.Id, resultado2.Value.Id);
    }

    [Fact]
    public async Task Handle_QuandoSaveChangesDevolveConflitoEALinhaExiste_DevolveReplayComDadosGravados()
    {
        var linhaExistente = new OperacaoRegistradaRow(
            "op-existente", "cliente-1", "td:tesouro-selic-2029", "aporte", 10m, 1000m,
            new DateOnly(2026, 6, 15), Agora, null);
        _readRepository.ConsultaPorId = Result<OperacaoConsulta>.Success(OperacaoConsulta.DeLinha(linhaExistente));
        _unitOfWork.FalhaAoSalvar = Result.Failure(DomainErrors.General.Conflict("conflito de teste"));

        var handler = CriarHandler(Result<bool>.Success(true));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.True(resultado.Value.Replay);
        Assert.Equal("op-existente", resultado.Value.Id);
    }

    [Fact]
    public async Task Handle_QuandoSaveChangesDevolveConflitoEALinhaNaoExiste_Devolve409EstornoJaRealizado()
    {
        _readRepository.ConsultaPorId = Result<OperacaoConsulta>.Success(OperacaoConsulta.NaoEncontrada);
        _unitOfWork.FalhaAoSalvar = Result.Failure(DomainErrors.General.Conflict("conflito de teste"));

        var handler = CriarHandler(Result<bool>.Success(true));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoJaRealizado, resultado.Error);
        Assert.Equal(ErrorType.Conflict, resultado.Error.Type);
    }

    [Fact]
    public async Task Handle_QuandoSaveChangesDevolveFalhaNaoConflito_RepassaOErroComoEsta()
    {
        _unitOfWork.FalhaAoSalvar = Result.Failure(OperacaoErrors.EstornoReferenciaInvalida);

        var handler = CriarHandler(Result<bool>.Success(true));

        var resultado = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OperacaoErrors.EstornoReferenciaInvalida, resultado.Error);

        Assert.Empty(_readRepository.ChamadasDeConsulta);
    }
}
