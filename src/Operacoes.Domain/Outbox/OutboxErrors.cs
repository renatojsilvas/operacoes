using Operacoes.Domain.Common;

namespace Operacoes.Domain.Outbox;

public static class OutboxErrors
{
    public static readonly Error TipoVazio =
        new("OutboxMessage.TipoVazio", "Tipo não deve ser vazio.");

    public static readonly Error RoutingKeyVazia =
        new("OutboxMessage.RoutingKeyVazia", "RoutingKey não deve ser vazia.");

    public static Error FalhaDeLeitura(string detail) =>
        new("Outbox.FalhaDeLeitura", detail);

    public static readonly Error BrokerIndisponivel =
        new("Outbox.BrokerIndisponivel", "Broker de mensageria indisponível para publicação da outbox.");

    public static readonly Error PublicacaoRejeitada =
        new("Outbox.PublicacaoRejeitada", "Broker de mensageria rejeitou a publicação da mensagem (nack).");

    public static readonly Error FalhaAoMarcarPublicado =
        new("Outbox.FalhaAoMarcarPublicado", "Falha ao marcar mensagens da outbox como publicadas.");
}
