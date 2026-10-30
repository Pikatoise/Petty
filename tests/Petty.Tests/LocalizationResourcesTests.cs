using System.Xml.Linq;

namespace Petty.Tests;

public class LocalizationResourcesTests
{
    [Fact]
    public void EnglishAndRussianResourceKeysMatch()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Petty.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var resources = Path.Combine(directory!.FullName, "src", "Petty.App", "Resources");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        static string[] Keys(string path, XNamespace ns) => XDocument.Load(path).Root!.Elements()
            .Select(element => (string?)element.Attribute(ns + "Key"))
            .Select(key => Assert.IsType<string>(key))
            .OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.Equal(Keys(Path.Combine(resources, "Strings.en.xaml"), xaml),
            Keys(Path.Combine(resources, "Strings.ru.xaml"), xaml));
    }
}
