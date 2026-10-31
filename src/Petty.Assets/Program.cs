using Petty.Assets;
using Petty.Core.Assets;
using System.Text.Json;

var outputDirectory = "assets/sprites";
var verifyOnly = false;
var regenerate = false;
string? packDirectory = null;
string? previewDirectory = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length: outputDirectory = args[++i]; break;
        case "--verify": verifyOnly = true; break;
        case "--regenerate": regenerate = true; break;
        case "--pack" when i + 1 < args.Length: packDirectory = args[++i]; break;
        case "--preview" when i + 1 < args.Length: previewDirectory = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("Petty.Assets [--out directory] [--verify|--regenerate] [--pack directory] [--preview directory]");
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
    var breeds = BreedDesign.Load(Path.Combine("assets", "breed-designs.json"));
    if (!verifyOnly) Directory.CreateDirectory(outputDirectory);
    if (verifyOnly && regenerate) throw new ArgumentException("--verify and --regenerate cannot be combined.");
    var shared = new[] { "tray-cat.png", "petty.ico", "bowl-empty.png", "bowl-full.png", "ball.png", "hand.png", "bowl-25.png", "bowl-50.png", "bowl-75.png", "pour-0.png", "pour-1.png", "pour-2.png", "pour-3.png", "pour-4.png", "pour-5.png" }.Select(f => Path.Combine(outputDirectory, f)).ToArray();
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
    if (!verifyOnly) BreedPackBuilder.Generate(breeds, outputDirectory, regenerate);
    if (previewDirectory is not null) BreedPackBuilder.WritePreview(breeds, previewDirectory);
    var passed = shared.All(File.Exists);
    foreach (var path in shared.Where(path => !File.Exists(path))) Console.Error.WriteLine($"Missing shared asset: {path}");
    if (passed)
    {
        // Props are stored at the shared root, independently of breed-pack directories.
        var report = AssetValidator.ValidateShared(outputDirectory);
        foreach (var error in report.Errors) Console.Error.WriteLine(error);
        passed &= report.Passed;
    }
    var packRoot = Path.Combine(outputDirectory, "packs");
    if (Directory.Exists(packRoot))
        passed &= ValidatePacks(Directory.EnumerateDirectories(packRoot), []) == 0;
    else { Console.Error.WriteLine("No breed packs found."); passed = false; }
    foreach (var breed in breeds)
        foreach (var coat in breed.Coats)
            if (!File.Exists(Path.Combine(packRoot, breed.Id, breed.Id + "-" + coat.Id + ".json")))
            { Console.Error.WriteLine($"Missing approved coat: {breed.Id}/{coat.Id}"); passed = false; }
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
        if (paths.Length is not (3 or 5))
        {
            Console.Error.WriteLine($"Pack {directory} must contain three legacy or five current coat metadata files.");
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

