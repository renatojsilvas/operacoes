using Operacoes.Domain.Operacoes;

namespace Operacoes.Domain.Tests.Operacoes;

public sealed class TipoOperacaoTests
{
    [Theory]
    [InlineData("aplicacao")]
    [InlineData("resgate")]
    [InlineData("aporte")]
    [InlineData("estorno")]
    public void FromName_ComNomeValido_DeveRetornarTipoCorrespondente(string name)
    {
        var result = TipoOperacao.FromName(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.Name);
    }

    [Theory]
    [InlineData("APLICACAO")]
    [InlineData("Resgate")]
    [InlineData("APORTE")]
    [InlineData("Estorno")]
    public void FromName_ComNomeEmCaixaDiferente_DeveSerCaseInsensitive(string name)
    {
        var result = TipoOperacao.FromName(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name.Trim().ToLowerInvariant(), result.Value.Name);
    }

    [Theory]
    [InlineData("  aplicacao  ")]
    [InlineData(" resgate")]
    [InlineData("aporte ")]
    public void FromName_ComEspacoEmVolta_DeveSerAparado(string name)
    {
        var result = TipoOperacao.FromName(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name.Trim().ToLowerInvariant(), result.Value.Name);
    }

    [Theory]
    [InlineData("compra")]
    [InlineData("aplicacoes")]
    public void FromName_ComNomeInvalido_DeveFalhar(string? name)
    {
        var result = TipoOperacao.FromName(name);

        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.TipoInvalido, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromName_ComNuloOuVazio_DeveFalhar(string? name)
    {
        var result = TipoOperacao.FromName(name);

        Assert.True(result.IsFailure);
        Assert.Equal(OperacaoErrors.TipoInvalido, result.Error);
    }

    [Fact]
    public void All_DeveConterExatamenteOsQuatroTipos()
    {
        Assert.Equal(4, TipoOperacao.All.Count);
        Assert.Contains(TipoOperacao.Aplicacao, TipoOperacao.All);
        Assert.Contains(TipoOperacao.Resgate, TipoOperacao.All);
        Assert.Contains(TipoOperacao.Aporte, TipoOperacao.All);
        Assert.Contains(TipoOperacao.Estorno, TipoOperacao.All);
    }
}
