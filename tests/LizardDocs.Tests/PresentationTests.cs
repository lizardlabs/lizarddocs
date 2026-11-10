using System.Text.Json;
using AngleSharp.Html.Parser;
using Jint;
using LizardDocs;

internal static class PresentationTests
{
    public static void Run(string temporary, Action<string, Action> test)
    {
        void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        Engine Script(string file)
        {
            var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(10)));
            engine.Execute("var module = { exports: {} };"); engine.Execute(ThemeAssets.Read(file)); return engine;
        }
        var project = Path.Combine(temporary, "presentation"); SiteInitializer.Create(project, "Presentation");
        var config = Path.Combine(project, "lizarddocs.yml"); var baseline = File.ReadAllText(config);
        test("external navigation and Markdown footer are independently configurable on every page", () => {
            File.WriteAllText(config, baseline + "\nsite_url: https://example.org/manual/\nexternal_links_new_tab: true\nshow_source_link: false\nrepo_url: https://github.com/example/docs\nedit_uri: edit/main/docs/\nfooter_links:\n  - Company: https://company.test/\n");
            File.AppendAllText(Path.Combine(project, "docs/index.md"), "\n[External](https://other.test/docs)\n[Same site](https://example.org/manual/guide/getting-started.html)\n[Protocol relative](//other.test/docs)\n[Email](mailto:hello@example.org)\n<a href='https://other.test/raw' rel='nofollow'>Raw link</a>\n");
            var result = SiteBuilder.Build(project); Assert(result.Errors.Count == 0, string.Join('\n', result.Errors));
            foreach (var file in Directory.GetFiles(Path.Combine(project, "site"), "*.html", SearchOption.AllDirectories)) {
                var page = new HtmlParser().ParseDocument(File.ReadAllText(file));
                Assert(page.QuerySelector("[data-source-link]") is null);
                foreach (var link in page.QuerySelectorAll("a[href]")) {
                    var href = link.GetAttribute("href")!;
                    var external = href.StartsWith("https://other.test/", StringComparison.Ordinal) || href.StartsWith("//other.test/", StringComparison.Ordinal) || href.StartsWith("https://github.com/", StringComparison.Ordinal) || href.StartsWith("https://company.test/", StringComparison.Ordinal);
                    Assert((link.GetAttribute("target") == "_blank") == external, href);
                    if (external) Assert(link.GetAttribute("rel")!.Contains("noopener") && link.GetAttribute("rel")!.Contains("noreferrer"));
                }
                var raw = page.QuerySelector("a[rel~='nofollow']");
                if (raw is not null) Assert(raw.GetAttribute("rel")!.Contains("noopener"));
            }
            File.WriteAllText(config, baseline + "\nrepo_url: https://github.com/example/docs\nedit_uri: edit/main/docs/\n");
            Assert(SiteBuilder.Build(project).Errors.Count == 0);
            var home = new HtmlParser().ParseDocument(File.ReadAllText(Path.Combine(project, "site/index.html")));
            Assert(home.QuerySelector("[data-source-link]") is not null && home.QuerySelector("a[target='_blank']") is null);
        });
        test("favicon consent settings and optional analytics are emitted safely without default third-party requests", () => {
            File.WriteAllText(config, baseline); Assert(SiteBuilder.Build(project).Errors.Count == 0);
            var initial = File.ReadAllText(Path.Combine(project, "site/index.html"));
            Assert(!initial.Contains("cookieconsent.min") && !initial.Contains("googletagmanager"));
            File.WriteAllText(Path.Combine(project, "docs/favicon.svg"), "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><path d='M0 0h10v10z'/></svg>");
            File.WriteAllText(config, baseline + "\nfavicon: favicon.svg\ngoogle_analytics_id: ''\ncookie_consent:\n  enabled: true\n  privacy_url: https://example.org/privacy/\n  popup_color: '#d86016'\n  button_color: '#102144'\n");
            Assert(SiteBuilder.Build(project).Errors.Count == 0);
            foreach (var file in Directory.GetFiles(Path.Combine(project, "site"), "*.html", SearchOption.AllDirectories)) {
                var page = new HtmlParser().ParseDocument(File.ReadAllText(file));
                Assert(page.QuerySelector("link[rel='icon']") is not null);
                Assert(page.QuerySelector("head script[src$='cookieconsent.min.js'][defer]") is not null);
                Assert(page.QuerySelector("head link[href$='cookieconsent.min.css']") is not null);
                Assert(page.QuerySelector("head script[src$='theme.js']:not([defer])") is not null);
                Assert(page.QuerySelector(".theme-switcher[hidden]") is not null);
                var settings = JsonDocument.Parse(page.GetElementById("lizarddocs-integrations")!.TextContent).RootElement;
                Assert(settings.GetProperty("analyticsId").GetString() == "");
                Assert(settings.GetProperty("popupColor").GetString() == "#d86016");
                Assert(page.QuerySelector("script[src*='google']") is null);
            }
            Assert(File.Exists(Path.Combine(project, "site/favicon.svg")));
            File.AppendAllText(config, "theme_switcher: false\n"); Assert(SiteBuilder.Build(project).Errors.Count == 0);
            var disabled = File.ReadAllText(Path.Combine(project, "site/index.html")); Assert(!disabled.Contains("theme.js") && !disabled.Contains("class=\"theme-switcher\""));
        });
        test("presentation configuration rejects invalid booleans analytics IDs assets and consent settings", () => {
            foreach (var option in new[] { "external_links_new_tab: maybe", "show_source_link: yes", "theme_switcher: null", "favicon: ../secret.svg", "favicon: https://other.test/icon.svg", "google_analytics_id: G-ABC123", "google_analytics_id: '</script>'", "cookie_consent: true", "cookie_consent:\n  enabled: true\n  privacy_url: javascript:alert(1)", "cookie_consent:\n  enabled: true\n  privacy_url: https://example.org/\n  popup_color: red", "cookie_consent:\n  enabled: yes" }) {
                File.WriteAllText(config, baseline + "\n" + option + "\n");
                var rejected = false; try { SiteConfiguration.Load(project); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, option);
            }
            File.WriteAllText(config, baseline + "\nfavicon: missing.svg\n"); Assert(DocumentationChecks.Check(project).Errors.Count > 0);
            File.WriteAllText(config, baseline + "\ngoogle_analytics_id: G-ABC123\ncookie_consent:\n  enabled: true\n  privacy_url: https://example.org/privacy/\n"); Assert(SiteConfiguration.Load(project).GoogleAnalyticsId == "G-ABC123");
        });
        test("theme control cycles system light dark persists choices and follows system only in automatic mode", () => {
            var engine = Script("theme.js");
            engine.Execute("""
                var saved = null, mediaChanged, clicked;
                var button = { hidden: true, firstElementChild: {}, setAttribute(key, value) { this[key] = value; }, addEventListener(name, callback) { clicked = callback; } };
                var doc = { documentElement: { dataset: {} }, readyState: 'complete', querySelector() { return button; } };
                var media = { matches: true, addEventListener(name, callback) { mediaChanged = callback; } };
                var host = { matchMedia() { return media; }, localStorage: { getItem() { return saved; }, setItem(key, value) { saved = value; } } };
                module.exports.initializeTheme(doc, host);
                """);
            Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'dark' && !button.hidden").AsBoolean());
            engine.Execute("clicked(); mediaChanged();"); Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'light' && saved === 'light'").AsBoolean());
            engine.Execute("clicked();"); Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'dark' && saved === 'dark'").AsBoolean());
            engine.Execute("clicked(); media.matches = false; mediaChanged();"); Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'light' && saved === 'system' && button['aria-label'].includes('system')").AsBoolean());
            engine.Execute("saved = 'dark'; module.exports.initializeTheme(doc, host);"); Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'dark'").AsBoolean());
            engine.Execute("Object.defineProperty(host, 'localStorage', { get() { throw Error('Blocked'); } }); module.exports.initializeTheme(doc, host); clicked();");
            Assert(engine.Evaluate("doc.documentElement.dataset.theme === 'light'").AsBoolean());
        });
        test("analytics stays unloaded until allowed handles revocation and is absent with empty ID or blocked consent library", () => {
            var engine = Script("integrations.js");
            engine.Execute("""
                var options, scripts = [], link = { attributes: {}, getAttribute() { return 'https://example.org/privacy/'; }, setAttribute(key, value) { this.attributes[key] = value; } };
                var settings = { analyticsId: '', privacyUrl: 'https://example.org/privacy/', popupColor: '#d86016', buttonColor: '#102144', externalLinksNewTab: true };
                var doc = { getElementById() { return { textContent: JSON.stringify(settings) }; }, createElement() { return {}; }, head: { append(script) { scripts.push(script); } }, querySelectorAll() { return [link]; } };
                var host = { cookieconsent: { initialise(value) { options = value; } } };
                module.exports.initializeIntegrations(doc, host); options.onInitialise('allow');
                """);
            Assert(engine.Evaluate("scripts.length === 0 && !host.dataLayer && options.type === 'info' && options.position === 'bottom-left' && options.theme === 'classic'").AsBoolean());
            engine.Execute("options.onPopupOpen();"); Assert(engine.Evaluate("link.attributes.target === '_blank' && link.attributes.rel === 'noopener noreferrer'").AsBoolean());
            engine.Execute("settings.analyticsId = 'G-ABC123'; module.exports.initializeIntegrations(doc, host); options.onInitialise('deny');");
            Assert(engine.Evaluate("options.type === 'opt-in' && scripts.length === 0 && !host.dataLayer").AsBoolean());
            engine.Execute("options.onStatusChange('allow'); options.onStatusChange('allow');");
            Assert(engine.Evaluate("scripts.length === 1 && scripts[0].src.endsWith('id=G-ABC123') && host['ga-disable-G-ABC123'] === false && host.dataLayer[0][2].ad_storage === 'denied'").AsBoolean());
            engine.Execute("options.onRevokeChoice();"); Assert(engine.Evaluate("host['ga-disable-G-ABC123'] === true && host.dataLayer[host.dataLayer.length - 1][2].analytics_storage === 'denied'").AsBoolean());
            engine.Execute("host = {}; scripts = []; module.exports.initializeIntegrations(doc, host);"); Assert(engine.Evaluate("scripts.length === 0 && !host.dataLayer").AsBoolean());
            engine.Execute("host = { cookieconsent: { initialise(value) { options = value; } } }; module.exports.initializeIntegrations(doc, host); options.onInitialise('allow');");
            Assert(engine.Evaluate("scripts.length === 1").AsBoolean(), "Persisted consent did not start analytics");
        });
        test("code colours meet readable contrast in both themes and use distinct editor-style token colours", () => {
            double Luminance(string hex) {
                double Channel(int offset) { var value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
                return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
            }
            foreach (var (background, colors) in new[] { ("#f7f9fa", new[] { "#0000ff", "#a31515", "#008000", "#075f36" }), ("#17212b", new[] { "#569cd6", "#ce9178", "#7fb36b", "#b5cea8" }) })
                foreach (var color in colors) {
                    var a = Luminance(color); var b = Luminance(background); Assert((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5, color);
                    Assert(ThemeAssets.Read("site.css").Contains(color, StringComparison.Ordinal));
                }
        });
    }
}
