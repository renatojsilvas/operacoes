namespace Operacoes.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
    Unavailable,
}
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Validation)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
