using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Operacoes.API.Extensions;

public static class HubConfigGuard
{
    private const string BaseUrlKey = "Hub:BaseUrl";
    private const string ApiKeyKey = "Hub:ApiKey";

    public static void Validate(string environmentName, string? baseUrl, string? apiKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trimmedBaseUrl = baseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{BaseUrlKey}' ('{trimmedBaseUrl}') não é uma URL http(s) " +
                $"absoluta válida em ambiente '{environmentName}'. Configure via variável de ambiente " +
                "Hub__BaseUrl (Docker/produção) ou dotnet user-secrets set \"Hub:BaseUrl\" \"<url>\" " +
                "--project src/Operacoes.API (dev local). Sem isto, HubCatalogoClient nunca consegue " +
                "validar instrumento nenhum e POST /v1/operacoes responde 503 para sempre.");
        }

        var trimmedApiKey = apiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedApiKey))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ApiKeyKey}' está vazia em ambiente '{environmentName}'. {ApiKeyHint}");
        }

        KeyStrengthGuard.EnsureStrong(ApiKeyKey, environmentName, trimmedApiKey, ApiKeyHint);
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(environment.EnvironmentName, configuration[BaseUrlKey], configuration[ApiKeyKey]);

    private const string ApiKeyHint =
        "É a chave que O OPERACOES ENVIA ao Hub (X-Api-Key) — diferente de ApiKey:Key, que é a chave " +
        "que o Operações EXIGE de quem o chama. Configure via variável de ambiente Hub__ApiKey " +
        "(Docker/produção) ou dotnet user-secrets set \"Hub:ApiKey\" \"<chave>\" --project src/Operacoes.API " +
        "(dev local). Sem isto, HubCatalogoClient recebe 401/403 do Hub e POST /v1/operacoes responde " +
        "503 para sempre.";
}
