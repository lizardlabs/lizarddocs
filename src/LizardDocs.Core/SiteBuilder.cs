using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LizardDocs;

public static class SiteBuilder
{
    public const string Marker = ".lizarddocs-site.json";
    public static CheckResult Build(string root, string? output = null, string? siteUrl = null, Func<string, CheckResult>? outputValidator = null)
    {
        root = Data.Resolve(root);
        var check = DocumentationChecks.Check(root);
        if (check.Errors.Count > 0) return check;
        var config = SiteConfiguration.Load(root);
        if (siteUrl is not null) config = config with { SiteUrl = SiteMetadata.NormalizeUrl(siteUrl) };
        output = Path.GetFullPath(output ?? Path.Combine(root, config.Output));
        if (Directory.Exists(output) && new DirectoryInfo(output).LinkTarget is not null) throw new InvalidDataException("Output cannot be a symbolic link");
        output = Data.Resolve(output);
        ValidateOutput(root, output, config);
        if (Directory.Exists(output) && !Owned(output, config))
            throw new InvalidDataException("Output is not a recognized generated site; choose an empty/new directory: " + output);
        var parent = Path.GetDirectoryName(output)!; Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".lizarddocs-site-stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(parent, ".lizarddocs-site-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var docs = config.SourcePath(root);
            var pages = config.Pages.ToArray();
            var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parser = new HtmlParser(); var search = new List<object>();
            foreach (var page in pages)
            {
                var source = page.Source!;
                if (SitePaths.Internal(source)) throw new InvalidDataException("Internal page cannot be published: " + source);
                var htmlPath = SitePaths.Html(source);
                if (!routes.Add(htmlPath)) throw new InvalidDataException("Conflicting page output: " + source);
                var markdown = new MarkdownPage(Data.Read(Path.Combine(docs, source)));
                var document = parser.ParseDocument("<!doctype html><html><body>" + markdown.Html + "</body></html>");
                foreach (var element in document.QuerySelectorAll("[href], [src]"))
                    foreach (var attribute in new[] { "href", "src" })
                        if (element.GetAttribute(attribute) is { } link)
                        {
                            CopyLinkedAsset(docs, stage, source, link);
                            element.SetAttribute(attribute, RewriteLink(source, htmlPath, link));
                        }
                var title = document.QuerySelector("h1")?.TextContent ?? page.Title;
                var headings = document.QuerySelectorAll("h2[id], h3[id], h4[id], h5[id], h6[id]")
                    .Select(heading => new { text = heading.TextContent, location = htmlPath + "#" + Uri.EscapeDataString(heading.Id!) }).ToArray();
                search.Add(new { location = htmlPath, title, headings, text = document.Body!.TextContent });
                Write(stage, htmlPath, Layout(config, pages, source, title, htmlPath, document.Body!.InnerHtml));
            }
            foreach (var asset in (Directory.Exists(Path.Combine(docs, "assets")) ? Directory.EnumerateFiles(Path.Combine(docs, "assets"), "*", SearchOption.AllDirectories) : []).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(docs, asset).Replace('\\', '/');
                if (!Data.Within(Path.Combine(docs, "assets"), Data.Resolve(asset)) || SitePaths.Internal(relative)) throw new InvalidDataException("Asset leaves public source: " + relative);
                var destination = Path.Combine(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(asset, destination, overwrite: true);
            }
            foreach (var asset in config.Styles.Concat(config.Scripts).Where(asset => !ThemeAssets.IsBuiltIn(asset))) CopyLinkedAsset(docs, stage, "index.md", asset);
            if (config.Logo.Length > 0) CopyLinkedAsset(docs, stage, "index.md", config.Logo);
            ThemeAssets.Write(stage);
            var searchJson = JsonSerializer.Serialize(new { docs = search });
            Write(stage, SiteSearch.JsonPath, searchJson);
            Write(stage, SiteSearch.ScriptPath, SiteSearch.Script(searchJson));
            Write(stage, "404.html", Layout(config, pages, null, "Page not found", "404.html", "<h1>Page not found</h1><p>Choose a page from the navigation or search the documentation.</p>"));
            if (config.SiteUrl.Length > 0)
            {
                Write(stage, "sitemap.xml", SiteMetadata.Sitemap(config));
                Write(stage, "robots.txt", "User-agent: *\nAllow: /\n\nSitemap: " + new Uri(new Uri(config.SiteUrl), "sitemap.xml").AbsoluteUri + "\n");
            }
            Write(stage, Marker, JsonSerializer.Serialize(new { generator = "LizardDocs", version = 1, pages = pages.Select(p => SitePaths.Html(p.Source!)) }));
            var result = SiteChecks.Check(stage);
            if (outputValidator is not null)
            {
                var extra = outputValidator(stage);
                result = result with { Errors = result.Errors.Concat(extra.Errors).ToArray() };
            }
            if (result.Errors.Count > 0) return result;
            if (Directory.Exists(output)) Directory.Move(output, backup);
            try { Directory.Move(stage, output); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, output); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            return new($"Built {pages.Length} public documentation pages with .NET at {output}. {result.Summary}", []);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    private static string RewriteLink(string source, string currentHtml, string link)
    {
        if (link.StartsWith('#') || link.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(link, UriKind.Absolute, out _)) return link;
        var marker = link.IndexOfAny(['?', '#']); var path = marker < 0 ? link : link[..marker]; var suffix = marker < 0 ? "" : link[marker..];
        if (path.Length == 0) return link;
        var logical = Path.GetRelativePath("/source", Path.GetFullPath(Path.Combine("/source", Path.GetDirectoryName(source) ?? "", Uri.UnescapeDataString(path)))).Replace('\\', '/');
        SitePaths.ValidateRelative(logical);
        var target = logical.EndsWith(".md", StringComparison.Ordinal) ? SitePaths.Html(logical) : logical;
        return SitePaths.RelativeUrl(currentHtml, target) + suffix;
    }

    private static string Layout(SiteConfiguration config, NavigationItem[] pages, string? source, string title, string htmlPath, string body)
        => SiteLayout.Render(config, pages, source, title, htmlPath, body);

    private static void Write(string root, string path, string content)
    {
        SitePaths.ValidateRelative(path);
        var destination = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination, content, new UTF8Encoding(false));
    }
    private static void CopyLinkedAsset(string docs, string stage, string source, string link)
    {
        if (link.StartsWith('#') || link.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(link, UriKind.Absolute, out _)) return;
        var address = link.Split('?', '#')[0];
        if (address.Length == 0 || address.EndsWith(".md", StringComparison.Ordinal)) return;
        var path = Data.Resolve(Path.Combine(docs, Path.GetDirectoryName(source) ?? "", Uri.UnescapeDataString(address)));
        if (!Data.Within(docs, path) || !File.Exists(path)) throw new InvalidDataException("Asset leaves source or is missing: " + link);
        var relative = Path.GetRelativePath(docs, path).Replace('\\', '/'); SitePaths.ValidateRelative(relative);
        if (SitePaths.Internal(relative)) throw new InvalidDataException("Hidden asset cannot be published: " + relative);
        var destination = Path.Combine(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination, overwrite: true);
    }

    private static bool Owned(string output, SiteConfiguration config)
    {
        if (!Directory.EnumerateFileSystemEntries(output).Any()) return true;
        foreach (var (name, generator) in new[] { (Marker, "LizardDocs"), (config.LegacyMarker, config.LegacyGenerator) })
        {
            if (name.Length == 0) continue;
            var marker = Path.Combine(output, name);
            if (File.Exists(marker) && Data.LoadObject(marker)["generator"]?.GetValue<string>() == generator) return true;
        }
        return false;
    }

    private static void ValidateOutput(string root, string output, SiteConfiguration config)
    {
        if (Data.Within(output, root) || Directory.GetParent(output) is null || File.Exists(output)) throw new InvalidDataException("Output must be a separate generated-site directory");
        if (Data.Within(config.SourcePath(root), output)) throw new InvalidDataException("Output overlaps documentation source: " + output);
        foreach (var source in new[] { "src", "tests", "tools", ".git", ".agents", ".codex" })
            if (Data.Within(Path.Combine(root, source), output)) throw new InvalidDataException("Output overlaps project source: " + output);
        if (Directory.Exists(output) && new DirectoryInfo(output).LinkTarget is not null) throw new InvalidDataException("Output cannot be a symbolic link");
    }
}
