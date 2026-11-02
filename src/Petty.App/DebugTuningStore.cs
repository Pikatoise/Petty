#if DEBUG
using System.IO;
using System.Text.Json;
using Petty.Core.Diagnostics;

namespace Petty.App;

internal sealed class DebugTuningStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DebugTuning Load()
    {
        if (!File.Exists(path))
        {
            return new();
        }

        string json = File.ReadAllText(path);
        DebugTuning tuning = JsonSerializer.Deserialize<DebugTuning>(json, Options) ?? new();
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        static double LegacyRate(JsonElement element) =>
            element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double value) && double.IsFinite(value)
                ? value / 60 : throw new JsonException("Invalid legacy debug decay rate.");
        // Files created before per-minute rates stored these two values per hour.
        if (!root.TryGetProperty("satietyDecayPerMinute", out _) && root.TryGetProperty("satietyDecay", out JsonElement satiety))
        {
            tuning = tuning with
            {
                SatietyDecayPerMinute = LegacyRate(satiety)
            };
        }

        if (!root.TryGetProperty("happinessDecayPerMinute", out _) && root.TryGetProperty("happinessDecay", out JsonElement happiness))
        {
            tuning = tuning with
            {
                HappinessDecayPerMinute = LegacyRate(happiness)
            };
        }

        if (!root.TryGetProperty("sleepMinSeconds", out _) && !root.TryGetProperty("sleepMaxSeconds", out _) &&
            root.TryGetProperty("sleepSeconds", out JsonElement oldSleep))
        {
            if (oldSleep.ValueKind != JsonValueKind.Number || !oldSleep.TryGetDouble(out double duration) || !double.IsFinite(duration))
            {
                throw new JsonException("Invalid legacy debug sleep duration.");
            }

            tuning = tuning with
            {
                SleepMinSeconds = Math.Min(120, duration),
                SleepMaxSeconds = duration
            };
        }
        return tuning.Sanitized();
    }

    public void Save(DebugTuning value)
    {
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value.Sanitized(), Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
#endif
