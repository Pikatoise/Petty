using System.IO;
using Microsoft.Win32;

namespace Petty.App.Desktop;

internal static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Petty";

    public static void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        Apply(key, enabled, Environment.ProcessPath ?? throw new IOException("Cannot locate the Petty executable."));
    }

    internal static void Apply(RegistryKey key, bool enabled, string executablePath)
    {
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath) ||
            !Path.GetFileName(executablePath).Equals("Petty.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Autostart requires an existing absolute Petty.exe path.");
        }

        string command = $"\"{Path.GetFullPath(executablePath)}\"";
        if (command.Length > 260)
        {
            throw new IOException("The Petty path exceeds the Windows Run key command length limit (260 characters).");
        }

        if (key.GetValue(ValueName) as string != command || key.GetValueKind(ValueName) != RegistryValueKind.String)
        {
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}
