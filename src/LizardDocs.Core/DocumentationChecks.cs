using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace LizardDocs;

public static class DocumentationChecks
{
    public static CheckResult Check(string root)
    {
        root = Data.Resolve(root); var config = SiteConfiguration.Load(root);
        var docs = config.SourcePath(root); var errors = new List<string>(); var links = 0;
        var pages = new Dictionary<string, MarkdownPage>(StringComparer.Ordinal);
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in config.Pages)
        {
            var source = item.Source!; SitePaths.ValidateRelative(source);
            var path = Data.Resolve(Path.Combine(docs, source));
            if (!Data.Within(docs, path) || !File.Exists(path)) { errors.Add("Navigation points to missing or escaped page: " + source); continue; }
            if (!pages.TryAdd(path, new MarkdownPage(Data.Read(path)))) errors.Add("Duplicate navigation page: " + source);
            if (!routes.Add(SitePaths.Html(source))) errors.Add("Conflicting page output: " + source);
        }
        if (!config.Pages.Any(page => page.Source == "index.md")) errors.Add("Navigation requires the homepage index.md");
        var parser = new HtmlParser();
        foreach (var (path, page) in pages)
        {
            var label = Path.GetRelativePath(docs, path).Replace('\\', '/');
            errors.AddRange(page.Errors.Select(error => label + ":" + error));
            var document = parser.ParseDocument(page.Html);
            foreach (var element in document.QuerySelectorAll("[href], [src]"))
            foreach (var attribute in new[] { "href", "src" })
            {
                if (element.GetAttribute(attribute) is not { } target) continue;
                if (Regex.IsMatch(target, @"^[A-Za-z][A-Za-z0-9+.-]*:") || target.StartsWith("//", StringComparison.Ordinal)) continue;
                links++;
                var hash = target.IndexOf('#'); var address = hash < 0 ? target : target[..hash];
                var fragment = hash < 0 ? "" : Uri.UnescapeDataString(target[(hash + 1)..]);
                var query = address.IndexOf('?'); if (query >= 0) address = address[..query];
                if (address.StartsWith('/')) { errors.Add($"{label}: use portable relative links: {target}"); continue; }
                var destination = address.Length == 0 ? path : Data.Resolve(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(address)));
                if (!Data.Within(docs, destination)) errors.Add($"{label}: link leaves site source: {target}");
                else if (!File.Exists(destination)) errors.Add($"{label}: missing link: {target}");
                else if (destination.EndsWith(".md", StringComparison.Ordinal) && !pages.ContainsKey(destination)) errors.Add($"{label}: linked Markdown page is not in navigation: {target}");
                else if (fragment.Length > 0 && pages.TryGetValue(destination, out var linked) && !linked.Anchors.Contains(fragment)) errors.Add($"{label}: missing anchor: {target}");
            }
        }
        foreach (var asset in config.Styles.Concat(config.Scripts).Where(asset => !ThemeAssets.IsBuiltIn(asset)).Concat(config.Logo.Length > 0 && !Uri.TryCreate(config.Logo, UriKind.Absolute, out _) ? new[] { config.Logo } : []).Concat(config.Favicon.Length > 0 ? new[] { config.Favicon } : []))
        {
            var path = Data.Resolve(Path.Combine(docs, asset));
            if (!Data.Within(docs, path) || !File.Exists(path)) errors.Add("Configured asset missing or outside source: " + asset);
        }
        foreach (var card in config.HomeCards)
            if (!config.Pages.Any(page => page.Source == card.Page)) errors.Add("Home card points to an unpublished page: " + card.Page);
        return new($"Checked {pages.Count} pages, {links} local links and configured assets.", errors);
    }
}
