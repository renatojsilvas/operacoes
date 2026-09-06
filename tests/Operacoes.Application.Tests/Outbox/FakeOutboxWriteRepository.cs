using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Tests.Outbox;

internal sealed class FakeOutboxWriteRepository : IOutboxWriteRepository
{
    public List<IReadOnlyList<long>> ChamadasMarcarPublicados { get; } = [];

    public List<DateTimeOffset> InstantesRecebidos { get; } = [];

    public List<long> IdsMarcados { get; } = [];

    public Result<int>? FalhaAoMarcar { get; set; }

    public int? AfetadosOverride { get; set; }

    public Task<Result> AdicionarAsync(OutboxMessage mensagem, CancellationToken ct) =>
        throw new NotSupportedException("O relay não adiciona mensagens à outbox.");

    public Task<Result<int>> MarcarPublicadosAsync(IReadOnlyList<long> ids, DateTimeOffset publicadoEm, CancellationToken ct)
    {
        ChamadasMarcarPublicados.Add(ids);
        InstantesRecebidos.Add(publicadoEm);

        if (FalhaAoMarcar is { IsFailure: true } falha)
        {
            return Task.FromResult(falha);
        }

        var afetados = AfetadosOverride ?? ids.Count;
        IdsMarcados.AddRange(ids.Take(afetados));
        return Task.FromResult(Result<int>.Success(afetados));
    }
}
