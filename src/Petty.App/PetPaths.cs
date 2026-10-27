using System.IO;

namespace Petty.App;

/// <summary>
/// Per-user writable locations. Kept out of the install directory so the app works
/// when installed under Program Files and so tests can redirect it with an
/// environment variable.
/// </summary>
internal sealed record PetPaths(string DataDirectory, string StateFile, string LogFile, string StartupReportFile, string FinalReportFile)
{
    public const string DataDirectoryVariable = "PETTY_DATA_DIR";

    public static PetPaths Resolve(string? overrideDirectory = null)
    {
        var directory = overrideDirectory;

        if (string.IsNullOrWhiteSpace(directory))
            directory = Environment.GetEnvironmentVariable(DataDirectoryVariable);

        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Petty");

        directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
        Directory.CreateDirectory(directory);

        return new PetPaths(
            directory,
            Path.Combine(directory, "pet-state.json"),
            Path.Combine(directory, "petty.log"),
            Path.Combine(directory, "startup-report.json"),
            Path.Combine(directory, "final-report.json"));
    }
}
