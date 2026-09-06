using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Operacoes.API.Extensions;

public static class ApiKeyGuard
{
    private const string ConfigKey = "ApiKey:Key";

    public static void Validate(string environmentName, string? configuredKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trimmedKey = configuredKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConfigKey}' está vazia em ambiente '{environmentName}'. {Hint}");
        }

        KeyStrengthGuard.EnsureStrong(ConfigKey, environmentName, trimmedKey, Hint);
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(environment.EnvironmentName, configuration[ConfigKey]);

    private const string Hint =
        "Configure a chave que o Operações exige de quem o chama via variável de ambiente ApiKey__Key " +
        "(Docker/produção) ou via dotnet user-secrets set \"ApiKey:Key\" \"<chave>\" --project src/Operacoes.API " +
        "(dev local).";
}
