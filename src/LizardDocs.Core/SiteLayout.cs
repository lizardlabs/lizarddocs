using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LizardDocs;

// Presentation is derived from the existing navigation and Markdown headings.
// The content and its stable IDs remain the author-maintained Markdown's own.
internal static class SiteLayout
{
    internal static string Render(SiteConfiguration config, NavigationItem[] pages, string? source, string title, string htmlPath, string body)
    {
        string E(string text) => WebUtility.HtmlEncode(text);
        string Url(string target) => E(SitePaths.RelativeUrl(htmlPath, target));
        bool Contains(NavigationItem item) => item.Source == source || item.Children.Any(Contains);
        string? First(NavigationItem item) => item.Source ?? item.Children.Select(First).FirstOrDefault(path => path is not null);
        var document = new HtmlParser().ParseDocument(body);
        var headings = document.QuerySelectorAll("h2[id], h3[id]");
        bool home = source == "index.md";
        var output = new StringBuilder($"<!doctype html><html lang=\"{E(config.Language)}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"generator\" content=\"LizardDocs\">");
        output.Append(SiteMetadata.Head(config, source, title, document));
        output.Append($"<meta name=\"theme-color\" content=\"{E(config.ThemeColor)}\">");
        foreach (var style in config.Styles) output.Append($"<link rel=\"stylesheet\" href=\"{Url(style)}\">");
        output.Append($"<style>:root{{--site-accent:{config.ThemeColor};--cs-accent:{config.ThemeColor};}}</style>");
        output.Append("</head><body><a class=\"skip-link\" href=\"#content\">Skip to content</a>");
        output.Append($"<header class=\"site-header\"><div class=\"site-header-inner\"><a class=\"site-brand\" href=\"{Url("index.html")}\">{(config.Logo.Length > 0 ? $"<img class=\"brand-logo\" alt=\"\" src=\"{(Uri.TryCreate(config.Logo, UriKind.Absolute, out _) ? E(config.Logo) : Url(config.Logo))}\">" : $"<span class=\"brand-mark\" aria-hidden=\"true\">{E(config.BrandMark)}</span>")}{E(config.Title)}<span class=\"brand-label\">{E(config.BrandLabel)}</span></a><nav class=\"site-sections\" aria-label=\"Site sections\">");
        foreach (var item in config.Navigation)
            if (First(item) is { } target)
                output.Append($"<a href=\"{Url(SitePaths.Html(target))}\"{(source is not null && Contains(item) ? " class=\"is-active\"" : "")}>{E(item.Title)}</a>");
        output.Append("</nav>");
        if (config.RepositoryUrl.Length > 0) output.Append($"<a class=\"site-repository\" href=\"{E(config.RepositoryUrl)}\">{E(config.RepositoryLabel)} <span aria-hidden=\"true\">↗</span></a>");
        output.Append("</div></header>");

        string Navigation(IEnumerable<NavigationItem> items)
        {
            var nav = new StringBuilder("<ul>");
            foreach (var item in items)
            {
                nav.Append("<li>");
                if (item.Children.Count > 0)
                {
                    nav.Append($"<details class=\"nav-section\"{(source is not null && Contains(item) ? " open" : "")}><summary>{E(item.Title)}</summary>");
                    nav.Append(Navigation(item.Children)); nav.Append("</details>");
                }
                else if (item.Source is { } path)
                    nav.Append($"<a href=\"{Url(SitePaths.Html(path))}\"{(path == source ? " aria-current=\"page\"" : "")}>{E(item.Title)}</a>");
                nav.Append("</li>");
            }
            return nav.Append("</ul>").ToString();
        }
        string Toc(string label)
        {
            var toc = new StringBuilder($"<nav aria-label=\"{label}\"><ol>");
            foreach (var heading in headings)
                toc.Append($"<li class=\"toc-{heading.LocalName}\"><a href=\"#{E(Uri.EscapeDataString(heading.Id!))}\">{E(heading.TextContent)}</a></li>");
            return toc.Append("</ol></nav>").ToString();
        }
        var navigation = Navigation(config.Navigation);
        output.Append($"<div class=\"site-layout{(headings.Length > 0 ? " has-toc" : "")}\"><aside class=\"site-sidebar\"><form role=\"search\" class=\"site-search\" data-search-index=\"{Url(SiteSearch.JsonPath)}\" data-search-script=\"{Url(SiteSearch.ScriptPath)}\" data-site-root=\"{Url("index.html")}\"><label for=\"site-search\">Search documentation</label><div class=\"search-field\"><input id=\"site-search\" name=\"q\" type=\"search\" autocomplete=\"off\" placeholder=\"Find a topic…\"><button type=\"submit\" aria-label=\"Search documentation\">↵</button></div><p class=\"search-status\" role=\"status\"></p><ol class=\"search-results\"></ol></form>");
        output.Append($"<nav class=\"site-navigation\" aria-label=\"Documentation\">{navigation}</nav><details class=\"site-mobile-navigation\"><summary>Browse documentation <span aria-hidden=\"true\">⌄</span></summary><nav aria-label=\"Mobile documentation\">{navigation}</nav></details>{(config.SidebarNote.Length > 0 ? $"<p class=\"sidebar-note\">{E(config.SidebarNote)}</p>" : "")}</aside>");
        output.Append("<main id=\"content\" tabindex=\"-1\">");
        if (source is not null)
        {
            output.Append("<nav class=\"site-breadcrumbs\" aria-label=\"Breadcrumb\"><ol>");
            void Breadcrumb(IEnumerable<NavigationItem> items)
            {
                foreach (var item in items.Where(Contains))
                {
                    output.Append($"<li>{E(item.Title)}</li>"); Breadcrumb(item.Children);
                }
            }
            Breadcrumb(config.Navigation); output.Append("</ol></nav>");
        }
        if (headings.Length > 0) output.Append($"<details class=\"site-inline-toc\"><summary>On this page</summary>{Toc("On this page, compact")}</details>");
        if (home && config.HomeCards.Count > 0)
        {
            var cards = document.CreateElement("section"); cards.ClassName = "site-start-grid"; cards.SetAttribute("aria-label", "Documentation paths");
            var number = 0;
            foreach (var card in config.HomeCards)
                cards.InnerHtml += $"<a href=\"{Url(SitePaths.Html(card.Page))}\"><span class=\"start-number\">{++number:00}</span><strong>{E(card.Title)}<span aria-hidden=\"true\">↗</span></strong><span>{E(card.Description)}</span></a>";
            document.Body!.InsertBefore(cards, document.Body.Children.FirstOrDefault(child => child.LocalName == "h2"));
        }
        output.Append($"<article{(home ? " class=\"site-home\"" : "")}>{document.Body!.InnerHtml}</article>");
        var index = Array.FindIndex(pages, page => page.Source == source);
        output.Append("<nav class=\"chapter-links\" aria-label=\"Adjacent pages\">");
        if (index > 0) output.Append($"<a rel=\"prev\" href=\"{Url(SitePaths.Html(pages[index - 1].Source!))}\"><span>Previous</span><strong>← {E(pages[index - 1].Title)}</strong></a>");
        if (index >= 0 && index + 1 < pages.Length) output.Append($"<a rel=\"next\" href=\"{Url(SitePaths.Html(pages[index + 1].Source!))}\"><span>Next</span><strong>{E(pages[index + 1].Title)} →</strong></a>");
        output.Append($"</nav><footer class=\"page-footer\"><span>{E(config.Title)}{(config.Tagline.Length > 0 ? " · " + E(config.Tagline) : "")}</span>");
        if (source is not null && config.RepositoryUrl.Length > 0 && config.EditUri.Length > 0)
        {
            var viewPath = config.EditUri.StartsWith("edit/", StringComparison.Ordinal) ? "blob/" + config.EditUri[5..] : config.EditUri;
            SitePaths.ValidateRelative(viewPath.TrimEnd('/'));
            var link = config.RepositoryUrl.TrimEnd('/') + "/" + viewPath.Trim('/') + "/" + string.Join('/', source.Split('/').Select(Uri.EscapeDataString));
            output.Append($"<a data-source-link href=\"{E(link)}\">View Markdown ↗</a>");
        }
        output.Append("</footer></main>");
        if (headings.Length > 0) output.Append($"<aside class=\"site-toc\"><p>On this page</p>{Toc("On this page")}</aside>");
        output.Append("</div>");
        if (config.FooterLinks.Count > 0)
        {
            output.Append($"<footer class=\"site-footer\"><div class=\"site-footer-inner\"><span>{E(config.Title)}</span><nav aria-label=\"Related sites and policies\">");
            foreach (var link in config.FooterLinks)
                output.Append($"<a href=\"{E(link.Url)}\">{E(link.Title)}</a>");
            output.Append("</nav></div></footer>");
        }
        foreach (var script in config.Scripts) output.Append($"<script src=\"{Url(script)}\" defer></script>");
        return output.Append("</body></html>\n").ToString();
    }
}
