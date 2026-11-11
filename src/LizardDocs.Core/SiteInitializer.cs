using System.Text.Json;

namespace LizardDocs;

public static class SiteInitializer
{
    public static CheckResult Create(string path, string name)
    {
        path = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) throw new ArgumentException("Site name must be nonempty and single-line");
        if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())) throw new InvalidDataException("New site requires a new or empty directory");
        if (new DirectoryInfo(path).LinkTarget is not null) throw new InvalidDataException("New site cannot replace a symbolic link");
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Cannot initialize a filesystem root"); Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".lizarddocs-new-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var existed = Directory.Exists(path);
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "docs/guide"));
            File.WriteAllText(Path.Combine(stage, ".gitignore"), "/site/\n/artifacts/\nlizarddocs.deploy.yml\n");
            File.WriteAllText(Path.Combine(stage, "lizarddocs.yml"), $"site_name: {JsonSerializer.Serialize(name)}\nsite_description: {JsonSerializer.Serialize("Documentation for " + name)}\nbrand_label: Docs\ntheme_color: '#0f766e'\ndocs_dir: docs\nsite_dir: site\nnav:\n  - Start:\n      - Overview: index.md\n      - Getting started: guide/getting-started.md\n");
            File.WriteAllText(Path.Combine(stage, "docs/index.md"), $"# {System.Net.WebUtility.HtmlEncode(name)}\n\nClear documentation, written in Markdown and published as ordinary HTML.\n\n## Start reading\n\n[Follow the guide](guide/getting-started.md).\n");
            File.WriteAllText(Path.Combine(stage, "docs/guide/getting-started.md"), "# Getting started\n\nEdit these Markdown files and rebuild whenever the documentation changes.\n\n## A small example\n\n```csharp\nConsole.WriteLine(\"Hello from your documentation\");\n```\n\n## Next steps\n\nAdd a page under docs/, then add its path to nav in lizarddocs.yml. Search, navigation and on-page contents are generated automatically.\n\n[Back to the overview](../index.md).\n");
            if (existed) Directory.Delete(path);
            Directory.Move(stage, path);
        }
        catch { if (existed && !Directory.Exists(path)) Directory.CreateDirectory(path); throw; }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
        return new("Created " + name + " at " + path + ". Run lizarddocs package --root " + JsonSerializer.Serialize(path) + " to build its HTML ZIP.", []);
    }
}
