using Operacoes.API.Extensions;

namespace Operacoes.API.Tests.Extensions;

public sealed class HubConfigGuardTests
{
    private const string ValidBaseUrl = "http://hub-precos-app:8080/";
    private const string ValidApiKey = "hub-api-key-com-mais-de-trinta-e-dois-caracteres";

    [Fact]
    public void Validate_Production_WithEmptyBaseUrl_ShouldThrowWithBaseUrlNameInMessage()
    {
        var act = () => HubConfigGuard.Validate("Production", "", ValidApiKey);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Hub:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithNullBaseUrl_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Production", null, ValidApiKey);

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithRelativeBaseUrl_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Production", "/v1/instruments", ValidApiKey);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Hub:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithNonHttpScheme_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Production", "ftp://hub-precos-app/", ValidApiKey);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Hub:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithValidHttpsBaseUrl_ShouldNotThrow()
    {
        HubConfigGuard.Validate("Production", "https://hub.example.com/", ValidApiKey);
    }

    [Fact]
    public void Validate_Production_WithEmptyApiKey_ShouldThrowWithApiKeyNameInMessage()
    {
        var act = () => HubConfigGuard.Validate("Production", ValidBaseUrl, "");

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Hub:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithNullApiKey_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Production", ValidBaseUrl, null);

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithWhitespaceApiKey_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Production", ValidBaseUrl, "   ");

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithValidBaseUrlAndApiKey_ShouldNotThrow()
    {
        HubConfigGuard.Validate("Production", ValidBaseUrl, ValidApiKey);
    }

    [Fact]
    public void Validate_Development_WithEmptyBaseUrlAndApiKey_ShouldNotThrow()
    {
        HubConfigGuard.Validate("Development", "", "");
    }

    [Fact]
    public void Validate_Testing_WithEmptyBaseUrlAndApiKey_ShouldNotThrow()
    {
        HubConfigGuard.Validate("Testing", "", "");
    }

    [Fact]
    public void Validate_UnknownEnvironment_WithEmptyBaseUrlAndApiKey_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate("Staging", "", "");

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithApiKeyBelowMinLength_ShouldThrowWithApiKeyNameInMessage()
    {
        var act = () => HubConfigGuard.Validate("Production", ValidBaseUrl, new string('a', 31));

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Hub:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("abaixo do mínimo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithApiKeyAtMinLength_ShouldNotThrow()
    {
        HubConfigGuard.Validate("Production", ValidBaseUrl, new string('a', 32));
    }

    [Theory]
    [InlineData("CHANGE-ME-IN-PRODUCTION")]
    [InlineData("dev-local-key")]
    [InlineData("uma-chave-qualquer-para-dev")]
    public void Validate_Production_WithBlockedPlaceholderApiKeyPaddedAboveMinLength_ShouldThrowForBlockedReason(
        string blockedPlaceholder)
    {
        var padded = blockedPlaceholder + new string('x', 40 - blockedPlaceholder.Length);
        Assert.True(padded.Length >= 32, "O padding precisa deixar a chave com 32+ caracteres.");

        var act = () => HubConfigGuard.Validate("Production", ValidBaseUrl, padded);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abaixo do mínimo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithBlockedPlaceholderApiKeyDifferentSeparator_ShouldThrow()
    {
        var act = () => HubConfigGuard.Validate(
            "Production", ValidBaseUrl, "change_me_in_production_padded_xxxx");

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_ExemptEnvironment_WithBlockedPlaceholderApiKey_ShouldNotThrow(string environmentName)
    {
        HubConfigGuard.Validate(environmentName, "", "CHANGE-ME-IN-PRODUCTION");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_ExemptEnvironment_WithShortApiKey_ShouldNotThrow(string environmentName)
    {
        HubConfigGuard.Validate(environmentName, "", "123");
    }
}
