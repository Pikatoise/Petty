namespace Petty.Core.Diagnostics;

/// <summary>
/// Log implementation that discards everything; useful as a neutral default.
/// </summary>
public sealed class NullAppLog: IAppLog
{
    public static NullAppLog Instance { get; } = new();

    private NullAppLog()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
