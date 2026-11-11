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
                Console.WriteLine("LizardDocs: Markdown to static HTML with .NET.\n\nCommands: new PATH [--name NAME], check, build, package, site-check, serve, deploy\nOptions: --root PATH, --output PATH, --archive PATH, --site-url URL, --port NUMBER\nDeployment: --deploy-config PATH, --dry-run\nBuild/package stay local. Deploy builds, validates and uploads using private FTP/FTPS settings.");
                return 0;
            }
            if (args[0] == "--version") { Console.WriteLine("LizardDocs " + typeof(Program).Assembly.GetName().Version!.ToString(3)); return 0; }
            if (args[0] == "new")
            {
                if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--name")) throw new ArgumentException("Expected: lizarddocs new PATH [--name NAME]");
                var made = SiteInitializer.Create(args[1], args.Length == 4 ? args[3] : Path.GetFileName(Path.GetFullPath(args[1])));
                Console.WriteLine(made.Summary); return 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var option = args[i];
                if (option == "--dry-run") { if (!options.TryAdd(option, "true")) throw new ArgumentException("Duplicate --dry-run"); continue; }
                if (i + 1 >= args.Length || option is not ("--root" or "--output" or "--archive" or "--site-url" or "--port" or "--deploy-config") || args[i + 1].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(option, args[++i]))
                    throw new ArgumentException("Expected unique --root, --output, --archive, --site-url, --port or --deploy-config values, or --dry-run");
            }
            var command = args[0];
            if (options.ContainsKey("--archive") && command != "package") throw new ArgumentException("--archive requires package");
            if (options.ContainsKey("--port") && command != "serve") throw new ArgumentException("--port requires serve");
            if (options.ContainsKey("--site-url") && command is not ("build" or "package" or "deploy")) throw new ArgumentException("--site-url requires build, package or deploy");
            if ((options.ContainsKey("--deploy-config") || options.ContainsKey("--dry-run")) && command != "deploy") throw new ArgumentException("Deployment options require deploy");
            if (options.ContainsKey("--output") && command == "check") throw new ArgumentException("--output requires build, package, site-check or serve");
            var root = options.TryGetValue("--root", out var project) ? Path.GetFullPath(project) : FindRoot();
            var output = options.GetValueOrDefault("--output");
            if (command == "deploy")
            {
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try {
                    var deployed = SiteDeployment.DeployAsync(root, output, options.GetValueOrDefault("--deploy-config"), options.GetValueOrDefault("--site-url"), options.ContainsKey("--dry-run"), cancellationToken: cancellation.Token).GetAwaiter().GetResult();
                    Console.WriteLine(deployed.Summary);
                    foreach (var error in deployed.Errors) Console.Error.WriteLine("FAIL: " + error);
                    return deployed.Errors.Count == 0 ? 0 : 1;
                } finally { Console.CancelKeyPress -= cancel; }
            }
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
        catch (OperationCanceledException) { Console.Error.WriteLine("LizardDocs: deployment cancelled. Previously replaced files may already be live."); return 130; }
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
