using Operacoes.Domain.Outbox;

namespace Operacoes.Domain.Tests.Outbox;

public sealed class OutboxMessageTests
{
    [Fact]
    public void Create_ComDadosValidos_DeveComecarPendente()
    {
        var result = OutboxMessage.Create("OperacaoRegistrada", "operacoes.registrada", "{}", DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PublicadoEm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComTipoVazio_DeveFalhar(string? tipo)
    {
        var result = OutboxMessage.Create(tipo!, "operacoes.registrada", "{}", DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OutboxErrors.TipoVazio, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComRoutingKeyVazia_DeveFalhar(string? routingKey)
    {
        var result = OutboxMessage.Create("OperacaoRegistrada", routingKey!, "{}", DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OutboxErrors.RoutingKeyVazia, result.Error);
    }

    [Fact]
    public void MarcarPublicado_DevePreencherPublicadoEmComOInstanteRecebido()
    {
        var mensagem = OutboxMessage.Create("OperacaoRegistrada", "operacoes.registrada", "{}", DateTimeOffset.UtcNow).Value;
        var quando = DateTimeOffset.UtcNow;

        mensagem.MarcarPublicado(quando);

        Assert.Equal(quando, mensagem.PublicadoEm);
    }
}
