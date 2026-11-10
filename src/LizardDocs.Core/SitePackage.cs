using System.IO.Compression;
using System.Text.Json;

namespace LizardDocs;

public static class SitePackage
{
    public static CheckResult Create(string root, string? output = null, string? archive = null, string? siteUrl = null, Func<string, CheckResult>? outputValidator = null)
    {
        root = Data.Resolve(root);
        var config = SiteConfiguration.Load(root);
        output = Path.GetFullPath(output ?? Path.Combine(root, config.Output));
        archive = Path.GetFullPath(archive ?? Path.Combine(root, "artifacts/lizarddocs-site.zip"));
        if (new FileInfo(archive).LinkTarget is not null) throw new InvalidDataException("Archive cannot be a symbolic link");
        archive = Data.Resolve(archive);
        if (!archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || Directory.Exists(archive) || Data.Within(Data.Resolve(output), archive))
            throw new InvalidDataException("Archive must be a separate .zip file outside the generated site");
        if (Data.Within(config.SourcePath(root), archive)) throw new InvalidDataException("Archive overlaps documentation source");
        foreach (var source in new[] { "src", "tests", "tools", ".git", ".agents", ".codex" })
            if (Data.Within(Path.Combine(root, source), archive)) throw new InvalidDataException("Archive overlaps repository source: " + archive);
        if (File.Exists(archive))
        {
            // A repeat build may replace our previous package, never an unrelated archive.
            using var previous = ZipFile.OpenRead(archive);
            bool owned = false;
            foreach (var (name, generator) in new[] { (SiteBuilder.Marker, "LizardDocs"), (config.LegacyMarker, config.LegacyGenerator) })
            {
                if (name.Length == 0 || previous.GetEntry(name) is not { } entry) continue;
                using var stream = entry.Open(); using var json = JsonDocument.Parse(stream);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("generator", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == generator) owned = true;
            }
            if (!owned) throw new InvalidDataException("Archive is not a recognized generated site: " + archive);        }
        var built = SiteBuilder.Build(root, output, siteUrl, outputValidator);
        if (built.Errors.Count > 0) return built;
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        var stage = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(stage, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    if (!Data.Within(output, Data.Resolve(file))) throw new InvalidDataException("Package file leaves generated site");
                    var path = Path.GetRelativePath(output, file).Replace('\\', '/');
                    SitePaths.ValidateRelative(path);
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var destination = entry.Open(); using var input = File.OpenRead(file); input.CopyTo(destination);
                }
            }
            File.Move(stage, archive, overwrite: true);
            return new($"{built.Summary}\nPackaged static site at {archive} ({new FileInfo(archive).Length:N0} bytes). Extract its contents into the site's web directory.", []);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
