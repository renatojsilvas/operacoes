using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Tests.Outbox;

internal sealed class FakeEventPublisher(Func<IReadOnlyList<OutboxPendente>, Result<int>>? publicar = null) : IEventPublisher
{
    private readonly Func<IReadOnlyList<OutboxPendente>, Result<int>> _publicar =
        publicar ?? (lote => Result<int>.Success(lote.Count));

    public List<IReadOnlyList<OutboxPendente>> LotesPublicados { get; } = [];

    public Task<Result<int>> PublicarAsync(IReadOnlyList<OutboxPendente> lote, CancellationToken ct)
    {
        LotesPublicados.Add(lote);
        return Task.FromResult(_publicar(lote));
    }
}
