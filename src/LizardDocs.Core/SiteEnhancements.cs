using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;

namespace LizardDocs;

internal static class SiteEnhancements
{
    internal static string Head(SiteConfiguration config, string htmlPath)
    {
        string Url(string path) => WebUtility.HtmlEncode(SitePaths.RelativeUrl(htmlPath, path));
        var head = new StringBuilder();
        if (config.Favicon.Length > 0) head.Append($"<link rel=\"icon\" href=\"{Url(config.Favicon)}\">");
        if (config.ThemeSwitcher) head.Append($"<script src=\"{Url(ThemeAssets.DirectoryName + "/theme.js")}\"></script>");
        if (config.CookieConsent.Enabled)
        {
            head.Append("<link rel=\"stylesheet\" href=\"https://cdnjs.cloudflare.com/ajax/libs/cookieconsent2/3.0.3/cookieconsent.min.css\">");
            head.Append("<script src=\"https://cdnjs.cloudflare.com/ajax/libs/cookieconsent2/3.0.3/cookieconsent.min.js\" defer></script>");
            var settings = JsonSerializer.Serialize(new { analyticsId = config.GoogleAnalyticsId, externalLinksNewTab = config.ExternalLinksNewTab,
                privacyUrl = config.CookieConsent.PrivacyUrl, popupColor = config.CookieConsent.PopupColor, buttonColor = config.CookieConsent.ButtonColor });
            head.Append($"<script id=\"lizarddocs-integrations\" type=\"application/json\">{settings}</script>");
            head.Append($"<script src=\"{Url(ThemeAssets.DirectoryName + "/integrations.js")}\" defer></script>");
        }
        return head.ToString();
    }

    internal static string ExternalLinks(SiteConfiguration config, string html)
    {
        if (!config.ExternalLinksNewTab) return html;
        Uri.TryCreate(config.SiteUrl, UriKind.Absolute, out var site);
        var document = new HtmlParser().ParseDocument(html);
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href")!;
            if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var target) || target.Scheme is not ("https" or "http")) continue;
            if (site is not null && site.Scheme == target.Scheme && site.Host == target.Host && site.Port == target.Port) continue;
            anchor.SetAttribute("target", "_blank");
            var rel = (anchor.GetAttribute("rel") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var token in new[] { "noopener", "noreferrer" })
                if (!rel.Contains(token, StringComparer.OrdinalIgnoreCase)) rel.Add(token);
            anchor.SetAttribute("rel", string.Join(' ', rel));
        }
        return "<!doctype html>" + document.DocumentElement.OuterHtml + "\n";
    }
}
