using LizardDocs;

namespace LizardDocs.Tool;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("LizardDocs: Markdown to static HTML with .NET.\n\nCommands: new PATH [--name NAME], check, build, package, site-check, serve\nOptions: --root PATH, --output PATH, --archive PATH, --site-url URL, --port NUMBER\nRun commands beside lizarddocs.yml or supply --root. Named HTML pages, local search and ZIP output are included.");
                return 0;
            }
            if (args[0] == "--version") { Console.WriteLine("LizardDocs 0.1.0"); return 0; }
            if (args[0] == "new")
            {
                if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--name")) throw new ArgumentException("Expected: lizarddocs new PATH [--name NAME]");
                var made = SiteInitializer.Create(args[1], args.Length == 4 ? args[3] : Path.GetFileName(Path.GetFullPath(args[1])));
                Console.WriteLine(made.Summary); return 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2)
                if (i + 1 >= args.Length || args[i] is not ("--root" or "--output" or "--archive" or "--site-url" or "--port") || args[i + 1].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Expected unique --root PATH, --output PATH, --archive PATH, --site-url URL or --port NUMBER options");
            var command = args[0];
            if (options.ContainsKey("--archive") && command != "package") throw new ArgumentException("--archive requires package");
            if (options.ContainsKey("--port") && command != "serve") throw new ArgumentException("--port requires serve");
            if (options.ContainsKey("--site-url") && command is not ("build" or "package")) throw new ArgumentException("--site-url requires build or package");
            if (options.ContainsKey("--output") && command == "check") throw new ArgumentException("--output requires build, package, site-check or serve");
            var root = options.TryGetValue("--root", out var project) ? Path.GetFullPath(project) : FindRoot();
            var output = options.GetValueOrDefault("--output");
            if (command == "serve")
            {
                if (!int.TryParse(options.GetValueOrDefault("--port", "8080"), out var port) || port is < 0 or > 65535) throw new ArgumentException("Invalid preview port");
                PreviewServer.Run(output ?? Path.Combine(root, SiteConfiguration.Load(root).Output), port); return 0;
            }
            var result = command switch
            {
                "check" => DocumentationChecks.Check(root),
                "build" => SiteBuilder.Build(root, output, options.GetValueOrDefault("--site-url")),
                "package" => SitePackage.Create(root, output, options.GetValueOrDefault("--archive"), options.GetValueOrDefault("--site-url")),
                "site-check" => SiteChecks.Check(output ?? Path.Combine(root, SiteConfiguration.Load(root).Output)),
                _ => throw new ArgumentException("Unknown command: " + command)
            };
            Console.WriteLine(result.Summary);
            foreach (var error in result.Errors) Console.Error.WriteLine("FAIL: " + error);
            return result.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or YamlDotNet.Core.YamlException)
        { Console.Error.WriteLine("LizardDocs: " + error.Message); return 1; }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (new[] { "lizarddocs.yml", "documentation.yml" }.Any(file => File.Exists(Path.Combine(directory.FullName, file)))) return directory.FullName;
        return Environment.CurrentDirectory;
    }
}
