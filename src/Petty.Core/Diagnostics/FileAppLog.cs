namespace Petty.Core.Diagnostics;

/// <summary>Tiny append-only log with size rotation; enough for diagnosing a desktop app.</summary>
public sealed class FileAppLog: IAppLog
{
    private const long MaxBytes = 512 * 1024;
    private readonly object _gate = new();
    private readonly string _path;

    public FileAppLog(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    public string FilePath => _path;

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (exception is not null) line += Environment.NewLine + exception;

        lock (_gate)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes) return;
            var previous = _path + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(_path, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore rotation failures.
        }
    }
}
