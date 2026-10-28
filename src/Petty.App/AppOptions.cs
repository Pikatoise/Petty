using System.Globalization;
using System.IO;
using Petty.App.Desktop;

namespace Petty.App;

/// <summary>
/// Command-line switches used by the prototype for diagnostics and automated
/// verification. Normal users never need them; the tray icon drives the app
/// (stage 4).
/// </summary>
internal sealed class AppOptions
{
    public string? DataDirectory { get; private init; }

    public TimeSpan? ExitAfter { get; private init; }

    /// <summary>Diagnostic switch: create this pet instead of showing the first-run wizard.</summary>
    public string? CreateName { get; private init; }

    /// <summary>How the pet window is placed relative to the desktop.</summary>
    public DesktopLayerMode DesktopLayer { get; private init; } = DesktopLayerMode.AboveDesktop;

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        string? dataDirectory = null;
        TimeSpan? exitAfter = null;
        string? createName = null;
        var desktopLayer = DesktopLayerMode.AboveDesktop;

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            var separator = argument.IndexOf('=');
            var name = separator < 0 ? argument : argument[..separator];
            var inlineValue = separator < 0 ? null : argument[(separator + 1)..];

            switch (name)
            {
                case "--data-dir":
                    dataDirectory = inlineValue ?? TakeValue(args, ref i, name);
                    break;

                case "--exit-after":
                    exitAfter = ParseDuration(inlineValue ?? TakeValue(args, ref i, name));
                    break;

                case "--create-name":
                    createName = inlineValue ?? TakeValue(args, ref i, name);
                    break;

                case "--desktop-layer":
                    desktopLayer = ParseLayer(inlineValue ?? TakeValue(args, ref i, name));
                    break;

                case "--no-desktop-layer":
                    if (inlineValue is not null)
                        throw new ArgumentException("'--no-desktop-layer' does not take a value.");
                    desktopLayer = DesktopLayerMode.Disabled;
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown argument '{argument}'." + Environment.NewLine +
                        "Supported: --data-dir <path>, --exit-after <seconds>, --create-name <name>, " +
                        "--desktop-layer <above|workerw|none>, --no-desktop-layer.");
            }
        }

        return new AppOptions
        {
            DataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory,
            ExitAfter = exitAfter,
            CreateName = string.IsNullOrWhiteSpace(createName) ? null : createName,
            DesktopLayer = desktopLayer,
        };
    }

    private static DesktopLayerMode ParseLayer(string value) => value.ToLowerInvariant() switch
    {
        "above" or "desktop" => DesktopLayerMode.AboveDesktop,
        "workerw" or "wallpaper" => DesktopLayerMode.WorkerW,
        "none" or "off" => DesktopLayerMode.Disabled,
        _ => throw new ArgumentException(
            $"'--desktop-layer' expects 'above', 'workerw' or 'none', got '{value}'."),
    };

    private static string TakeValue(IReadOnlyList<string> args, ref int index, string name)
    {
        if (index + 1 >= args.Count)
            throw new ArgumentException($"'{name}' requires a value.");

        return args[++index];
    }

    private static TimeSpan ParseDuration(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0 ||
            seconds > 3600)
        {
            throw new ArgumentException($"'--exit-after' expects seconds between 0 and 3600, got '{value}'.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
