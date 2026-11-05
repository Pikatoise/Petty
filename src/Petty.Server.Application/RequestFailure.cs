namespace Petty.Server.Application;

public enum FailureKind
{
    Invalid,
    Unauthorized,
    Conflict
}

public sealed class RequestFailure(FailureKind kind, string code) : Exception(code)
{
    public FailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
