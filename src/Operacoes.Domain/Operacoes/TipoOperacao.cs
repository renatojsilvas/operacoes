using Operacoes.Domain.Common;

namespace Operacoes.Domain.Operacoes;

public sealed record TipoOperacao
{
    public static readonly TipoOperacao Aplicacao = new("aplicacao");
    public static readonly TipoOperacao Resgate = new("resgate");
    public static readonly TipoOperacao Aporte = new("aporte");
    public static readonly TipoOperacao Estorno = new("estorno");

    public static IReadOnlyCollection<TipoOperacao> All { get; } =
        [Aplicacao, Resgate, Aporte, Estorno];

    private TipoOperacao(string name) => Name = name;

    public string Name { get; }

    public static Result<TipoOperacao> FromName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var match = All.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? match
            : OperacaoErrors.TipoInvalido;
    }
}
