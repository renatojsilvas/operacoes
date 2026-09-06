using Operacoes.Application.Catalogo;
using Operacoes.Application.Common.Interfaces;
using Operacoes.Application.Operacoes;
using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Tests.Operacoes;

internal sealed class FakeHubCatalogoClient(Result<bool> resultado) : IHubCatalogoClient
{
    public List<string> Chamadas { get; } = [];

    public Task<Result<bool>> InstrumentoExisteAsync(string instrumentoId, CancellationToken ct)
    {
        Chamadas.Add(instrumentoId);
        return Task.FromResult(resultado);
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
