using System.Text;

namespace Operacoes.API.Extensions;

internal static class KeyStrengthGuard
{
    internal const int MinKeyLength = 32;

    internal static readonly string[] BlockedKeys =
    {
        "CHANGE-ME-IN-PRODUCTION",
        "dev-local-key",
        "uma-chave-qualquer-para-dev",
    };

    private static readonly char[] SeparatorChars = ['-', '_', '.', ' '];

    private static readonly string[] BlockedKeysCanonical =
        BlockedKeys.Select(RemoveSeparators).ToArray();

    internal static void EnsureStrong(string configKey, string environmentName, string trimmedKey, string hint)
    {
        var canonicalKey = RemoveSeparators(trimmedKey);

        if (BlockedKeysCanonical.Any(blocked => canonicalKey.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{configKey}' contém um placeholder conhecido em ambiente " +
                $"'{environmentName}', mesmo que como parte de um valor maior ou com separadores " +
                $"diferentes (-, _, ., espaço). Placeholders proibidos: {string.Join(", ", BlockedKeys)}. {hint}");
        }

        if (trimmedKey.Length < MinKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{configKey}' tem {trimmedKey.Length} caracteres em ambiente " +
                $"'{environmentName}', abaixo do mínimo de {MinKeyLength}. {hint} Para gerar uma chave forte: " +
                "openssl rand -hex 32.");
        }
    }

    private static string RemoveSeparators(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (Array.IndexOf(SeparatorChars, c) < 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
