namespace Petty.Server.Application;

public enum FailureKind
{
    Invalid,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed class RequestFailure(FailureKind kind, string code, IReadOnlyDictionary<string, object?>? details = null) : Exception(code)
{
    public FailureKind Kind { get; } = kind;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}
