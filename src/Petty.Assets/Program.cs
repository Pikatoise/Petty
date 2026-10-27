using Petty.Assets;

var outputDirectory = "assets/sprites";
var verifyOnly = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outputDirectory = args[++i];
            break;
        case "--verify":
            verifyOnly = true;
            break;
        case "-h":
        case "--help":
            Console.WriteLine("Petty.Assets — generates and validates the cat sprite atlas.");
            Console.WriteLine("  (no args)        generate atlas, frames and previews into assets/sprites");
            Console.WriteLine("  --out <dir>      write to a different directory");
            Console.WriteLine("  --verify         validate an existing atlas without regenerating it");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
    }
}

if (!verifyOnly)
{
    Console.WriteLine("Generating cat sprite atlas...");
    var build = AtlasBuilder.Build();
    AtlasWriter.Write(build, outputDirectory);
    Console.WriteLine();
}

var report = AssetValidator.ValidateDirectory(outputDirectory);

foreach (var note in report.Notes) Console.WriteLine($"  . {note}");
foreach (var error in report.Errors) Console.WriteLine($"  ! {error}");

Console.WriteLine();
Console.WriteLine(report.Passed ? "ASSET VALIDATION PASSED" : "ASSET VALIDATION FAILED");
return report.Passed ? 0 : 1;
