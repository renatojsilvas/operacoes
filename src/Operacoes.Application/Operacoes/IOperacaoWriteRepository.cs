using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;

namespace Operacoes.Application.Operacoes;

public interface IOperacaoWriteRepository
{
    Task<Result> AdicionarAsync(Operacao operacao, CancellationToken ct);
}
