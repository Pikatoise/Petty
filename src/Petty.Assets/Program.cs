using System.Text.Json;
using Petty.Assets;
using Petty.Core.Assets;

var outputDirectory = "assets/sprites";
var verifyOnly = false;
var regenerate = false;
string? packDirectory = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length: outputDirectory = args[++i]; break;
        case "--verify": verifyOnly = true; break;
        case "--regenerate": regenerate = true; break;
        case "--pack" when i + 1 < args.Length: packDirectory = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("Petty.Assets [--out directory] [--verify|--regenerate] [--pack directory]");
            return 0;
        default: Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 2;
    }
}

try
{
    if (packDirectory is not null)
    {
        if (!verifyOnly || regenerate) throw new ArgumentException("--pack requires --verify.");
        return ValidatePacks([packDirectory], []);
    }
    var variants = Variant.Load(Path.Combine("assets", "sprites", "catalog.json"));
    if (!verifyOnly) Directory.CreateDirectory(outputDirectory);
    if (verifyOnly && regenerate) throw new ArgumentException("--verify and --regenerate cannot be combined.");
    var shared = new[] { "tray-cat.png", "petty.ico", "bowl-empty.png", "bowl-full.png", "ball.png" }.Select(f => Path.Combine(outputDirectory, f)).ToArray();
    if (!verifyOnly && (regenerate || shared.Any(path => !File.Exists(path))))
    {
        var temporary = Directory.CreateTempSubdirectory("petty-assets-");
        try
        {
            TrayIconBuilder.Write(temporary.FullName);
            TrayIconBuilder.WriteIcon(temporary.FullName);
            PropSpriteBuilder.Write(temporary.FullName);
            foreach (var path in shared)
                AtlasWriter.WriteIfChanged(path, File.ReadAllBytes(Path.Combine(temporary.FullName, Path.GetFileName(path))));
        }
        finally { temporary.Delete(recursive: true); }
    }
    var passed = true;
    foreach (var variant in variants)
    {
        var files = new[] { Path.Combine(outputDirectory, variant.Id + ".png"), Path.Combine(outputDirectory, variant.Id + ".json") };
        var source = variant.Source is null ? null : new[]
        {
            Path.Combine("assets", "sprites", variant.Source, variant.Id + ".png"),
            Path.Combine("assets", "sprites", variant.Source, variant.Id + ".json"),
        };
        ValidationReport? report = null;
        try { report = AssetValidator.ValidateDirectory(outputDirectory, variant); }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or NullReferenceException or InvalidOperationException or JsonException) { Console.Error.WriteLine(ex.Message); }
        if (!verifyOnly && (regenerate || report?.Passed != true))
        {
            Console.WriteLine($"Generating {variant.Id}...");
            if (source is null) AtlasWriter.Write(AtlasBuilder.Build(variant), outputDirectory);
            else for (var i = 0; i < files.Length; i++) AtlasWriter.WriteIfChanged(files[i], File.ReadAllBytes(source[i]));
            report = AssetValidator.ValidateDirectory(outputDirectory, variant);
        }
        foreach (var error in report?.Errors ?? ["Asset validation failed."]) Console.Error.WriteLine($"{variant.Id}: {error}");
        if (report?.Passed != true || shared.Any(path => !File.Exists(path))) passed = false;
    }
    var packRoot = Path.Combine(outputDirectory, "packs");
    if (Directory.Exists(packRoot))
        passed &= ValidatePacks(Directory.EnumerateDirectories(packRoot), variants.Select(v => v.Id)) == 0;
    Console.WriteLine(passed ? "ASSET VALIDATION PASSED" : "ASSET VALIDATION FAILED");
    return passed ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int ValidatePacks(IEnumerable<string> directories, IEnumerable<string> existingIds)
{
    var ids = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
    var passed = true;
    foreach (var directory in directories.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
    {
        if (!SpriteMetadataValidator.IsValidId(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))))
        {
            Console.Error.WriteLine($"Invalid pack directory: {directory}");
            passed = false;
            continue;
        }
        var paths = Directory.EnumerateFiles(directory, "*.json").ToArray();
        if (paths.Length != 3)
        {
            Console.Error.WriteLine($"Pack {directory} must contain exactly three metadata files.");
            passed = false;
        }
        foreach (var png in Directory.EnumerateFiles(directory, "*.png"))
            if (!File.Exists(Path.ChangeExtension(png, ".json")))
            {
                Console.Error.WriteLine($"Atlas has no matching metadata: {png}");
                passed = false;
            }
        foreach (var path in paths)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!ids.Add(id)) { Console.Error.WriteLine($"Duplicate sprite ID: {id}"); passed = false; continue; }
            var report = AssetValidator.ValidateAtlas(directory, id);
            foreach (var error in report.Errors) Console.Error.WriteLine($"{id}: {error}");
            passed &= report.Passed;
        }
    }
    Console.WriteLine(passed ? "PACK VALIDATION PASSED" : "PACK VALIDATION FAILED");
    return passed ? 0 : 1;
}

