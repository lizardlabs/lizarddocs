using YamlDotNet.RepresentationModel;

namespace LizardDocs;

public sealed record NavigationItem(string Title, string? Source, IReadOnlyList<NavigationItem> Children);
public sealed record FooterLink(string Title, string Url);
public sealed record HomeCard(string Title, string Description, string Page);

public sealed record SiteConfiguration(string Title, string Description, string Output, IReadOnlyList<NavigationItem> Navigation,
    IReadOnlyList<string> Styles, IReadOnlyList<string> Scripts, string RepositoryUrl, string EditUri)
{
    public string SiteUrl { get; init; } = "";
    public string SourceDirectory { get; init; } = "docs";
    public string BrandMark { get; init; } = "";
    public string BrandLabel { get; init; } = "Docs";
    public string Tagline { get; init; } = "";
    public string SidebarNote { get; init; } = "";
    public string Logo { get; init; } = "";
    public string ThemeColor { get; init; } = "#0f766e";
    public string RepositoryLabel { get; init; } = "Repository";
    public string Language { get; init; } = "en";
    public string LegacyMarker { get; init; } = "";
    public string LegacyGenerator { get; init; } = "";
    public IReadOnlyList<HomeCard> HomeCards { get; init; } = [];
    public string SourcePath(string root)
    {
        var source = Data.Resolve(Path.Combine(Data.Resolve(root), SourceDirectory));
        if (!Data.Within(Data.Resolve(root), source)) throw new InvalidDataException("Source directory leaves project root");
        if (!Directory.Exists(source)) throw new InvalidDataException("Source directory missing: " + SourceDirectory);
        return source;
    }
    public static string ConfigPath(string root)
    {
        foreach (var name in new[] { "lizarddocs.yml", "documentation.yml" })
            if (File.Exists(Path.Combine(root, name))) return Path.Combine(root, name);
        throw new InvalidDataException("Missing lizarddocs.yml (documentation.yml is also accepted)");
    }
    public IReadOnlyList<FooterLink> FooterLinks { get; init; } = [];
    public IEnumerable<NavigationItem> Pages => Flatten(Navigation);
    private static IEnumerable<NavigationItem> Flatten(IEnumerable<NavigationItem> items)
    {
        foreach (var item in items)
        {
            if (item.Source is not null) yield return item;
            foreach (var child in Flatten(item.Children)) yield return child;
        }
    }
    public static SiteConfiguration Load(string root)
    {
        var yaml = new YamlStream(); yaml.Load(new StringReader(Data.Read(ConfigPath(root))));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
            throw new InvalidDataException("Expected one documentation configuration mapping");
        string Scalar(string name, string? fallback = null) => mapping.Children.TryGetValue(new YamlScalarNode(name), out var node) && node is YamlScalarNode scalar
            ? scalar.Value ?? throw new InvalidDataException("Missing value: " + name) : fallback ?? throw new InvalidDataException("Missing configuration: " + name);
        string[] List(string name)
        {
            if (!mapping.Children.TryGetValue(new YamlScalarNode(name), out var node)) return [];
            if (node is not YamlSequenceNode sequence) throw new InvalidDataException("Expected an asset list: " + name);
            return sequence.Children.Select(child => child is YamlScalarNode scalar && scalar.Value is { Length: > 0 } text ? text : throw new InvalidDataException("Invalid asset: " + name)).ToArray();
        }
        var docsDirectory = Scalar("docs_dir", "docs"); SitePaths.ValidateRelative(docsDirectory);
        if (!mapping.Children.TryGetValue(new YamlScalarNode("nav"), out var navigation)) throw new InvalidDataException("Missing navigation");
        var styles = new[] { ThemeAssets.DirectoryName + "/code.css", ThemeAssets.DirectoryName + "/site.css" }.Concat(List("extra_css")).Distinct().ToArray();
        var scripts = List("extra_javascript").Concat(new[] { ThemeAssets.DirectoryName + "/site.js", ThemeAssets.DirectoryName + "/search.js" }).Distinct().ToArray();
        foreach (var asset in styles.Concat(scripts)) SitePaths.ValidateRelative(asset);
        var repository = Scalar("repo_url", "");
        if (repository.Length > 0 && (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)) throw new InvalidDataException("Expected an HTTP repository URL");
        var footer = new List<FooterLink>();
        if (mapping.Children.TryGetValue(new YamlScalarNode("footer_links"), out var footerNode))
        {
            if (footerNode is not YamlSequenceNode footerSequence) throw new InvalidDataException("Expected a footer link list");
            foreach (var node in footerSequence.Children)
            {
                if (node is not YamlMappingNode entry || entry.Children.Count != 1 ||
                    entry.Children.Single().Key is not YamlScalarNode { Value: { Length: > 0 } label } ||
                    entry.Children.Single().Value is not YamlScalarNode { Value: { Length: > 0 } address } ||
                    !Uri.TryCreate(address, UriKind.Absolute, out var link) || link.Scheme is not ("https" or "http") || link.UserInfo.Length > 0)
                    throw new InvalidDataException("Footer links require a label and an absolute HTTP(S) URL without credentials");
                if (footer.Any(item => string.Equals(item.Title, label, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Duplicate footer link: " + label);
                footer.Add(new(label, link.AbsoluteUri));
            }
        }
        var color = Scalar("theme_color", "#0f766e");
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, @"\A#[0-9a-fA-F]{6}\z")) throw new InvalidDataException("theme_color must be #RRGGBB");
        var logo = Scalar("logo", "");
        if (logo.Length > 0)
        {
            if (Uri.TryCreate(logo, UriKind.Absolute, out var image))
            { if (image.Scheme is not ("http" or "https") || image.UserInfo.Length > 0) throw new InvalidDataException("Logo must use HTTP(S) or a local relative path"); }
            else SitePaths.ValidateRelative(logo);
        }
        var cards = new List<HomeCard>();
        if (mapping.Children.TryGetValue(new YamlScalarNode("home_cards"), out var cardNode))
        {
            if (cardNode is not YamlSequenceNode cardSequence) throw new InvalidDataException("home_cards must be a list");
            foreach (var child in cardSequence)
            {
                if (child is not YamlMappingNode card) throw new InvalidDataException("Invalid home card");
                string Field(string key) => card.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode { Value: { Length: > 0 } field } ? field : throw new InvalidDataException("Home card requires " + key);
                var page = Field("page"); SitePaths.ValidateRelative(page);
                cards.Add(new(Field("title"), Field("description"), page));
            }
        }
        var legacyMarker = Scalar("legacy_marker", ""); var legacyGenerator = Scalar("legacy_generator", "");
        if ((legacyMarker.Length > 0) != (legacyGenerator.Length > 0)) throw new InvalidDataException("Legacy adoption requires both legacy_marker and legacy_generator");
        if (legacyMarker.Length > 0 && (Path.GetFileName(legacyMarker) != legacyMarker || legacyMarker.Contains('\\') || legacyMarker.Contains(':'))) throw new InvalidDataException("Legacy marker must be a filename");
        var title = Scalar("site_name");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("site_name must not be empty");
        return new(title, Scalar("site_description", ""), Scalar("site_dir", "site"), ParseNavigation(navigation), styles, scripts, repository, Scalar("edit_uri", ""))
        {
            SiteUrl = SiteMetadata.NormalizeUrl(Scalar("site_url", "")), FooterLinks = footer, SourceDirectory = docsDirectory,
            BrandMark = Scalar("brand_mark", title[..1]), BrandLabel = Scalar("brand_label", "Docs"), Tagline = Scalar("tagline", ""),
            SidebarNote = Scalar("sidebar_note", ""), Logo = logo, ThemeColor = color, RepositoryLabel = Scalar("repository_label", "Repository"),
            Language = Scalar("language", "en"), HomeCards = cards, LegacyMarker = legacyMarker, LegacyGenerator = legacyGenerator
        };
    }
    private static NavigationItem[] ParseNavigation(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence) throw new InvalidDataException("Navigation must be a sequence");
        return sequence.Children.Select(child =>
        {
            if (child is not YamlMappingNode entry || entry.Children.Count != 1) throw new InvalidDataException("Navigation items need one title");
            var pair = entry.Children.Single(); var title = pair.Key.ToString();
            return pair.Value is YamlScalarNode scalar && scalar.Value is { Length: > 0 } source
                ? new NavigationItem(title, source, []) : new NavigationItem(title, null, ParseNavigation(pair.Value));
        }).ToArray();
    }
}

public static class SitePaths
{
    public static string Route(string source)
    {
        ValidateRelative(source);
        if (!source.EndsWith(".md", StringComparison.Ordinal)) throw new InvalidDataException("Expected Markdown source: " + source);
        if (source == "index.md") return "";
        return source.EndsWith("/index.md", StringComparison.Ordinal) ? source[..^9] + ".html" : source[..^3] + ".html";
    }
    public static string Html(string source) => source == "index.md" ? "index.html" : Route(source);
    public static void ValidateRelative(string path)
    {
        if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid public site path: " + path);
    }
    public static bool Internal(string path) => path.Replace('\\', '/').Split('/').Any(part => part.StartsWith('.'));
    public static string RelativeUrl(string currentHtml, string target)
    {
        var relative = Path.GetRelativePath(Path.GetDirectoryName(currentHtml) is { Length: > 0 } directory ? directory : ".", target).Replace('\\', '/');
        return string.Join('/', relative.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
    }
}
