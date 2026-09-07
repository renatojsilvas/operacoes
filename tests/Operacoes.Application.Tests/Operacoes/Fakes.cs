using Operacoes.Application.Catalogo;
using Operacoes.Application.Common.Interfaces;
using Operacoes.Application.Operacoes;
using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Tests.Operacoes;

internal sealed class FakeHubCatalogoClient : IHubCatalogoClient
{
    private readonly Result<bool> _resultado;

    public FakeHubCatalogoClient()
    {
        _resultado = Result<bool>.Success(true);
    }

    public FakeHubCatalogoClient(Result<bool> resultado)
    {
        _resultado = resultado;
    }

    public IReadOnlyList<InstrumentoCatalogo>? Catalogo { get; set; }

    public List<string> Chamadas { get; } = [];

    public Task<Result<IReadOnlyList<InstrumentoCatalogo>>> BuscarPorTermoAsync(string termo, CancellationToken ct)
    {
        Chamadas.Add(termo);

        if (_resultado.IsFailure)
        {
            return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Failure(_resultado.Error));
        }

        if (Catalogo is not null)
        {
            return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Success(Catalogo));
        }

        IReadOnlyList<InstrumentoCatalogo> itens = _resultado.Value
            ? [new InstrumentoCatalogo(termo, "titulo-publico", termo, Vencido: false)]
            : [];

        return Task.FromResult(Result<IReadOnlyList<InstrumentoCatalogo>>.Success(itens));
    }
}

internal sealed class FakeOperacaoReadRepository : IOperacaoReadRepository
{
    public Result<bool> ReferenciaDeEstornoValida { get; set; } = Result<bool>.Success(true);

    public Result<OperacaoConsulta> ConsultaPorId { get; set; } = Result<OperacaoConsulta>.Success(OperacaoConsulta.NaoEncontrada);

    public List<(string EstornaOperacaoId, string ClienteId, string InstrumentoId)> ChamadasDeReferencia { get; } = [];

    public List<string> ChamadasDeConsulta { get; } = [];

    public Task<Result<bool>> ExisteComoReferenciaDeEstornoAsync(
        string estornaOperacaoId, string clienteId, string instrumentoId, CancellationToken ct)
    {
        ChamadasDeReferencia.Add((estornaOperacaoId, clienteId, instrumentoId));
        return Task.FromResult(ReferenciaDeEstornoValida);
    }

    public Task<Result<OperacaoConsulta>> ObterPorIdAsync(string id, CancellationToken ct)
    {
        ChamadasDeConsulta.Add(id);
        return Task.FromResult(ConsultaPorId);
    }

    public Result<IReadOnlyList<string>> InstrumentosNegociados { get; set; } =
        Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

    public List<string> ChamadasDeNegociados { get; } = [];

    public Task<Result<IReadOnlyList<string>>> ObterInstrumentosNegociadosAsync(string clienteId, CancellationToken ct)
    {
        ChamadasDeNegociados.Add(clienteId);
        return Task.FromResult(InstrumentosNegociados);
    }
}

internal sealed class FakeOperacaoWriteRepository : IOperacaoWriteRepository
{
    public List<Operacao> Adicionadas { get; } = [];

    public Task<Result> AdicionarAsync(Operacao operacao, CancellationToken ct)
    {
        Adicionadas.Add(operacao);
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakeOutboxWriteRepository : IOutboxWriteRepository
{
    public List<OutboxMessage> Adicionadas { get; } = [];

    public Task<Result> AdicionarAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        Adicionadas.Add(mensagem);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<int>> MarcarPublicadosAsync(IReadOnlyList<long> ids, DateTimeOffset publicadoEm, CancellationToken ct) =>
        Task.FromResult(Result<int>.Success(ids.Count));
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public int LimparRastreamentoCalls { get; private set; }

    public Result? FalhaAoSalvar { get; set; }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.FromResult(FalhaAoSalvar ?? Result.Success());
    }

    public void LimparRastreamento()
    {
        LimparRastreamentoCalls++;
    }
}
