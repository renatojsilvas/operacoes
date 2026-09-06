using System.Text;
using Operacoes.Application.Operacoes;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Domain.Outbox;
using Operacoes.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Operacoes.API.Tests.Integration;

public sealed class RabbitMqEventPublisherIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

    public Task InitializeAsync() => _rabbitMq.StartAsync();

    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();

    private RabbitMqConnectionProvider CriarConnectionProvider(string exchange)
    {
        var uri = new Uri(_rabbitMq.GetConnectionString());
        var credenciais = uri.UserInfo.Split(':', 2);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = uri.Host,
                ["RabbitMq:Port"] = uri.Port.ToString(),
                ["RabbitMq:User"] = credenciais[0],
                ["RabbitMq:Password"] = credenciais[1],
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Exchange"] = exchange
            })
            .Build();

        return new RabbitMqConnectionProvider(configuration, NullLogger<RabbitMqConnectionProvider>.Instance);
    }

    private static string CriarPayloadDeContrato()
    {
        var agora = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var operacao = Operacao.Create(
            id: "operacao-1",
            clienteId: "cliente-1",
            instrumentoId: "td:tesouro-selic-2029",
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1234.56m,
            dataEvento: new DateOnly(2026, 6, 15),
            registradoEm: agora,
            hoje: new DateOnly(2026, 6, 15)).Value;

        return TradeRegisteredPayload.Serializar(operacao);
    }

    [Fact]
    public async Task ObterConexaoAsync_DeclaraExchangeComoTopicDuravelSemAutoDelete()
    {
        const string exchange = "prices-topologia";

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoTipo = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false));
            Assert.NotNull(excecaoTipo);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoDurable = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: false, autoDelete: false));
            Assert.NotNull(excecaoDurable);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoAutoDelete = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: true));
            Assert.NotNull(excecaoAutoDelete);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoParametrosCorretos = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false));
            Assert.Null(excecaoParametrosCorretos);
        }
    }

    [Fact]
    public async Task PublicarAsync_MensagemChegaNaFilaComRoutingKeyPropriedadesEPayloadIntegros()
    {
        const string exchange = "prices-propriedades";
        const string routingKey = "trades.registered";
        var payload = CriarPayloadDeContrato();

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using var channel = await connection.CreateChannelAsync();
        var fila = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(fila.QueueName, exchange, routingKey);

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);
        var mensagem = new OutboxPendente(4242, TradeRegisteredPayload.Tipo, routingKey, payload);

        var resultado = await publisher.PublicarAsync([mensagem], CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value);

        var entrega = await channel.BasicGetAsync(fila.QueueName, autoAck: true);

        Assert.NotNull(entrega);
        Assert.Equal(routingKey, entrega!.RoutingKey);
        Assert.True(entrega.BasicProperties.Persistent);
        Assert.Equal(TradeRegisteredPayload.Tipo, entrega.BasicProperties.Type);
        Assert.Equal("4242", entrega.BasicProperties.MessageId);
        Assert.Equal(payload, Encoding.UTF8.GetString(entrega.Body.Span));
    }

    [Fact]
    public async Task PublicarAsync_LoteDeVariasMensagens_DevolveTodasConfirmadas()
    {
        const string exchange = "prices-lote";
        const int quantidade = 5;

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using var channel = await connection.CreateChannelAsync();
        var fila = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(fila.QueueName, exchange, "trades.registered.lote.#");

        var lote = Enumerable.Range(1, quantidade)
            .Select(i => new OutboxPendente(i, TradeRegisteredPayload.Tipo, $"trades.registered.lote.{i}", "{}"))
            .ToList();

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);
        var resultado = await publisher.PublicarAsync(lote, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(quantidade, resultado.Value);

        var recebidas = 0;
        for (var i = 0; i < quantidade; i++)
        {
            var entrega = await channel.BasicGetAsync(fila.QueueName, autoAck: true);
            if (entrega is not null)
            {
                recebidas++;
            }
        }

        Assert.Equal(quantidade, recebidas);
    }

    [Fact]
    public async Task PublicarAsync_BrokerInacessivel_DevolveResultFailureBrokerIndisponivelSemLancar()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = "127.0.0.1",
                ["RabbitMq:Port"] = "1",
                ["RabbitMq:User"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Exchange"] = "prices-indisponivel"
            })
            .Build();

        await using var connectionProvider =
            new RabbitMqConnectionProvider(configuration, NullLogger<RabbitMqConnectionProvider>.Instance);
        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);

        var lote = new[] { new OutboxPendente(1, TradeRegisteredPayload.Tipo, "trades.registered.indisponivel", "{}") };

        Result<int>? resultado = null;
        var excecao = await Record.ExceptionAsync(async () =>
            resultado = await publisher.PublicarAsync(lote, CancellationToken.None));

        Assert.Null(excecao);
        Assert.NotNull(resultado);
        Assert.True(resultado!.IsFailure);
        Assert.Equal(OutboxErrors.BrokerIndisponivel.Code, resultado.Error.Code);
    }

    private async Task<(RabbitMqConnectionProvider ConnectionProvider, string FilaRestrita, string FilaLivre)>
        CriarTopologiaComFilaRestritaAsync(string exchange, string routingKeyRestrita, string routingKeyLivre)
    {
        var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using (var channel = await connection.CreateChannelAsync())
        {
            await channel.QueueDeclareAsync(
                queue: "restrita",
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-max-length"] = 0, ["x-overflow"] = "reject-publish" });
            await channel.QueueBindAsync("restrita", exchange, routingKeyRestrita);

            await channel.QueueDeclareAsync(queue: "livre", durable: false, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync("livre", exchange, routingKeyLivre);
        }

        return (connectionProvider, "restrita", "livre");
    }

    [Fact]
    public async Task PublicarAsync_MesmoLoteRepetidoTresVezesComPrimeiraMensagemVenenosa_NaoAmplificaFilaLivre()
    {
        const string exchange = "prices-veneno-head-of-line";
        const string routingKeyRestrita = "trades.registered.veneno.restrita";
        const string routingKeyLivre = "trades.registered.veneno.livre";

        var (connectionProvider, _, filaLivre) =
            await CriarTopologiaComFilaRestritaAsync(exchange, routingKeyRestrita, routingKeyLivre);
        await using var _ = connectionProvider;

        var lote = new[]
        {
            new OutboxPendente(1, TradeRegisteredPayload.Tipo, routingKeyRestrita, "{}"),
            new OutboxPendente(2, TradeRegisteredPayload.Tipo, routingKeyLivre, "{}"),
            new OutboxPendente(3, TradeRegisteredPayload.Tipo, routingKeyLivre, "{}")
        };

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);

        for (var ciclo = 0; ciclo < 3; ciclo++)
        {
            var resultado = await publisher.PublicarAsync(lote, CancellationToken.None);

            Assert.True(resultado.IsFailure);
            Assert.Equal(OutboxErrors.PublicacaoRejeitada.Code, resultado.Error.Code);
        }

        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        var recebidasNaFilaLivre = 0;
        while (await channel.BasicGetAsync(filaLivre, autoAck: true) is not null)
        {
            recebidasNaFilaLivre++;
        }

        Assert.Equal(0, recebidasNaFilaLivre);
    }

    [Fact]
    public async Task PublicarAsync_FalhaNoMeioDoLote_ConfirmadosIgualAsMensagensRealmenteEntreguesNaFilaLivre()
    {
        const string exchange = "prices-veneno-meio-do-lote";
        const string routingKeyRestrita = "trades.registered.meio.restrita";
        const string routingKeyLivre = "trades.registered.meio.livre";

        var (connectionProvider, _, filaLivre) =
            await CriarTopologiaComFilaRestritaAsync(exchange, routingKeyRestrita, routingKeyLivre);
        await using var _ = connectionProvider;

        var lote = new[]
        {
            new OutboxPendente(1, TradeRegisteredPayload.Tipo, routingKeyLivre, "{}"),
            new OutboxPendente(2, TradeRegisteredPayload.Tipo, routingKeyLivre, "{}"),
            new OutboxPendente(3, TradeRegisteredPayload.Tipo, routingKeyRestrita, "{}"),
            new OutboxPendente(4, TradeRegisteredPayload.Tipo, routingKeyLivre, "{}")
        };

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);
        var resultado = await publisher.PublicarAsync(lote, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value);

        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        var recebidasNaFilaLivre = 0;
        while (await channel.BasicGetAsync(filaLivre, autoAck: true) is not null)
        {
            recebidasNaFilaLivre++;
        }

        Assert.Equal(resultado.Value, recebidasNaFilaLivre);
    }

    [Fact]
    public async Task PublicarAsync_FilaDestinoRejeitaComNack_DevolveResultFailurePublicacaoRejeitadaSemLancar()
    {
        const string exchange = "prices-nack-primeira-mensagem";
        const string routingKeyRestrita = "trades.registered.nack.restrita";
        const string routingKeyLivre = "trades.registered.nack.livre";

        var (connectionProvider, _, filaLivre) =
            await CriarTopologiaComFilaRestritaAsync(exchange, routingKeyRestrita, routingKeyLivre);
        await using var _ = connectionProvider;

        var lote = new[] { new OutboxPendente(1, TradeRegisteredPayload.Tipo, routingKeyRestrita, "{}") };

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);

        Result<int>? resultado = null;
        var excecao = await Record.ExceptionAsync(async () =>
            resultado = await publisher.PublicarAsync(lote, CancellationToken.None));

        Assert.Null(excecao);
        Assert.NotNull(resultado);
        Assert.True(resultado!.IsFailure);
        Assert.Equal(OutboxErrors.PublicacaoRejeitada.Code, resultado.Error.Code);
    }
}
