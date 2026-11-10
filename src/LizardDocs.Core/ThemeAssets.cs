namespace LizardDocs;

public static class ThemeAssets
{
    public const string DirectoryName = "assets/lizarddocs";
    public static IReadOnlyList<string> Files { get; } = new[] { "site.css", "code.css", "site.js", "search.js" };
    public static bool IsBuiltIn(string path) => Files.Any(file => path == DirectoryName + "/" + file);
    public static string Read(string file)
    {
        if (!Files.Contains(file)) throw new ArgumentException("Unknown theme asset: " + file);
        using var stream = typeof(ThemeAssets).Assembly.GetManifestResourceStream("LizardDocs.Core.Assets." + file) ?? throw new InvalidDataException("Theme asset missing: " + file);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    internal static void Write(string output)
    {
        var directory = Path.Combine(output, DirectoryName); System.IO.Directory.CreateDirectory(directory);
        foreach (var file in Files) File.WriteAllText(Path.Combine(directory, file), Read(file));
    }
}
