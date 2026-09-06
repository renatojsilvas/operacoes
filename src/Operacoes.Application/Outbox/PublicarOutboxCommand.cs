using Operacoes.Domain.Common;
using MediatR;

namespace Operacoes.Application.Outbox;

public sealed record PublicarOutboxCommand : IRequest<Result<RelayResultado>>;
