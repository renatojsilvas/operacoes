using System.Security.Cryptography;
using System.Text;

namespace Operacoes.Domain.Operacoes;

public static class OperacaoIdempotencyId
{
    private const string Prefixo = "op-";

    private const int TamanhoHex = 32;

    public static string Derive(string clienteId, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(clienteId);
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        var entrada = $"{clienteId.Length}:{clienteId}:{idempotencyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(entrada));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return Prefixo + hex[..TamanhoHex];
    }
}
