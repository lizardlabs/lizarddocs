using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;

namespace LizardDocs;

internal static class SiteMetadata
{
    internal static string NormalizeUrl(string value)
    {
        if (value.Length == 0) return "";
        if (value.Any(char.IsWhiteSpace) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Site URL must be an absolute HTTP(S) address without credentials, query or fragment");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    internal static string PageUrl(string siteUrl, string source) => new Uri(new Uri(siteUrl),
        string.Join('/', SitePaths.Route(source).Split('/').Select(Uri.EscapeDataString))).AbsoluteUri;

    internal static string Head(SiteConfiguration config, string? source, string title, IDocument document)
    {
        string E(string value) => WebUtility.HtmlEncode(value);
        var pageTitle = title == config.Title ? title + " documentation" : title + " — " + config.Title;
        // Only introductory prose belongs in a search snippet, never an example or a generated menu.
        var introduction = document.QuerySelectorAll("body > p").FirstOrDefault(paragraph =>
            paragraph.TextContent.Trim().Length >= 60)?.TextContent;
        var description = Regex.Replace(introduction ?? title + ". " + config.Description, @"\s+", " ").Trim();
        if (description.Length > 165)
        {
            var boundary = description.LastIndexOf(' ', 162, 45);
            description = description[..(boundary >= 0 ? boundary : 162)].TrimEnd() + "…";
        }
        var head = new StringBuilder($"<title>{E(pageTitle)}</title><meta name=\"description\" content=\"{E(description)}\">");
        head.Append($"<meta property=\"og:type\" content=\"website\"><meta property=\"og:site_name\" content=\"{E(config.Title)}\"><meta property=\"og:title\" content=\"{E(pageTitle)}\"><meta property=\"og:description\" content=\"{E(description)}\">");
        head.Append($"<meta name=\"twitter:card\" content=\"summary\"><meta name=\"twitter:title\" content=\"{E(pageTitle)}\"><meta name=\"twitter:description\" content=\"{E(description)}\">");
        if (source is null) head.Append("<meta name=\"robots\" content=\"noindex\">");
        else if (config.SiteUrl.Length > 0)
        {
            var canonical = E(PageUrl(config.SiteUrl, source));
            head.Append($"<link rel=\"canonical\" href=\"{canonical}\"><meta property=\"og:url\" content=\"{canonical}\">");
        }
        return head.ToString();
    }

    internal static string Sitemap(SiteConfiguration config)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        return new XDocument(new XElement(ns + "urlset", config.Pages.Select(page =>
            new XElement(ns + "url", new XElement(ns + "loc", PageUrl(config.SiteUrl, page.Source!)))))).ToString() + "\n";
    }
}
