using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LizardDocs;

public static class SiteChecks
{
    private static readonly HashSet<string> MarkupTags = ("a abbr address area article aside audio b base bdi bdo blockquote body br button canvas caption cite code col colgroup data datalist dd del details dfn dialog div dl dt em embed fieldset figcaption figure footer form h1 h2 h3 h4 h5 h6 head header hgroup hr html i iframe img input ins kbd label legend li link main map mark menu meta meter nav noscript object ol optgroup option output p picture pre progress q rp rt ruby s samp script search section select slot small source span strong style sub summary sup table tbody td template textarea tfoot th thead time title tr track u ul var video wbr svg animate animatemotion animatetransform circle clippath defs desc ellipse feblend fecolormatrix fecomponenttransfer fecomposite feconvolvematrix fediffuselighting fedisplacementmap fedistantlight fedropshadow feflood fefunca fefuncb fefuncg fefuncr fegaussianblur feimage femerge femergenode femorphology feoffset fepointlight fespecularlighting fespotlight fetile feturbulence filter foreignobject g image line lineargradient marker mask metadata mpath path pattern polygon polyline radialgradient rect set stop switch symbol text textpath tspan use view math annotation annotation-xml maction menclose merror mfenced mfrac mi mmultiscripts mn mo mover mpadded mphantom mprescripts mroot mrow ms mspace msqrt mstyle msub msubsup msup mtable mtd mtext mtr munder munderover semantics").Split(' ').ToHashSet(StringComparer.Ordinal);

    public static CheckResult Check(string output)
    {
        output = Data.Resolve(output); var errors = new List<string>(); var parser = new HtmlParser();
        var files = Directory.GetFiles(output, "*.html", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) errors.Add("Generated HTML pages missing");
        var pages = files.ToDictionary(path => path, path => parser.ParseDocument(Data.Read(path)), StringComparer.Ordinal);
        foreach (var (path, document) in pages)
        {
            var relative = Path.GetRelativePath(output, path).Replace('\\', '/');
            if (SitePaths.Internal(relative)) errors.Add("Hidden page rendered: " + relative);
            foreach (var tag in document.All.Select(element => element.LocalName.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
                if (!MarkupTags.Contains(tag) && Regex.IsMatch(tag, @"\A[a-z_][a-z0-9_.]*\??\z")) errors.Add($"Unescaped type-like HTML tag <{tag}>: {relative}; use inline code for type syntax");
            foreach (var element in document.QuerySelectorAll("a[href], link[href], script[src], img[src], form[data-search-script]"))
            {
                var link = element.GetAttribute(element.LocalName switch { "form" => "data-search-script", "script" or "img" => "src", _ => "href" })!;
                if (Regex.IsMatch(link, @"^[A-Za-z][A-Za-z0-9+.-]*:") || link.StartsWith("//", StringComparison.Ordinal)) continue;
                var hash = link.IndexOf('#'); var address = hash < 0 ? link : link[..hash];
                var fragment = hash < 0 ? "" : Uri.UnescapeDataString(link[(hash + 1)..]);
                var query = address.IndexOf('?'); if (query >= 0) address = address[..query];
                var destination = address.Length == 0 ? path : address.StartsWith('/')
                    ? Data.Resolve(Path.Combine(output, Uri.UnescapeDataString(address).TrimStart('/')))
                    : Data.Resolve(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(address)));
                if (Directory.Exists(destination)) destination = Path.Combine(destination, "index.html");
                if (!Data.Within(output, destination)) errors.Add($"Link escapes site: {relative} -> {link}");
                else if (!File.Exists(destination)) errors.Add($"Broken site link/asset: {relative} -> {link}");
                else if (fragment.Length > 0 && pages.TryGetValue(destination, out var target) && target.GetElementById(fragment) is null) errors.Add($"Broken site fragment: {relative} -> {link}");
            }
        }
        var searchPath = Path.Combine(output, SiteSearch.JsonPath);
        if (!File.Exists(searchPath)) errors.Add("Search index missing");
        else
        {
            var scriptPath = Path.Combine(output, SiteSearch.ScriptPath);
            if (!File.Exists(scriptPath)) errors.Add("Browser search index missing");
            else if (Data.Read(scriptPath) != SiteSearch.Script(Data.Read(searchPath))) errors.Add("Browser search index differs from JSON index");
            var index = Data.LoadObject(searchPath); var docs = Data.Array(index, "docs");
            if (docs.Count == 0) errors.Add("Search index empty");
            foreach (var node in docs)
            {
                var entry = node as JsonObject ?? throw new InvalidDataException("Invalid search record");
                var location = Data.Text(entry, "location", true);
                if (SitePaths.Internal(location.Split('#', 2)[0])) errors.Add("Hidden record indexed");
                var address = location.Split('#', 2)[0].Split('?', 2)[0];
                if (Uri.TryCreate(address, UriKind.Absolute, out _) || address.StartsWith("//", StringComparison.Ordinal)) errors.Add("Non-public search location: " + location);
                else
                {
                    var target = Data.Resolve(Path.Combine(output, Uri.UnescapeDataString(address)));
                    if (Directory.Exists(target)) target = Path.Combine(target, "index.html");
                    if (!Data.Within(output, target) || !pages.ContainsKey(target)) errors.Add("Broken search location: " + location);
                    else if (entry.ContainsKey("headings"))
                    {
                        foreach (var nodeHeading in Data.Array(entry, "headings"))
                        {
                            var heading = nodeHeading as JsonObject ?? throw new InvalidDataException("Invalid search heading");
                            var headingLocation = Data.Text(heading, "location");
                            var hash = headingLocation.IndexOf('#');
                            var element = hash < 0 ? null : pages[target].GetElementById(Uri.UnescapeDataString(headingLocation[(hash + 1)..]));
                            if (hash < 0 || headingLocation[..hash] != address || element?.LocalName is not ("h2" or "h3" or "h4" or "h5" or "h6")
                                || element.TextContent != Data.Text(heading, "text")) errors.Add("Broken search heading: " + headingLocation);
                        }
                    }
                }
            }
        }
        foreach (var file in ThemeAssets.Files)
            if (!File.Exists(Path.Combine(output, ThemeAssets.DirectoryName, file))) errors.Add("Theme asset missing: " + file);
        var count = pages.Values.Sum(document => document.QuerySelectorAll("pre code").Length);
        return new($"Checked {files.Length} generated HTML pages, public-only search index and assets; {count} code blocks available for highlighting/copying.", errors);
    }
}
