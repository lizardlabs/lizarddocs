using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using Jint;
using LizardDocs;
using Cli = LizardDocs.Tool.Program;

var passed = 0; var failed = 0;
void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); } }
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Pass(CheckResult result) => Assert(result.Errors.Count == 0, string.Join('\n', result.Errors));
void Reject(Action action) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException) { return; } throw new Exception("Unsafe input was accepted"); }
string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
var temporary = Path.Combine(Path.GetTempPath(), "lizarddocs-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
var project = Path.Combine(temporary, "atlas"); var output = Path.Combine(project, "site");
try
{
    SearchTests.Run(temporary, Test);
    PresentationTests.Run(temporary, Test);
    Test("new creates a standalone project and protects existing content", () => {
        Pass(SiteInitializer.Create(project, "Atlas: API & Docs")); Pass(DocumentationChecks.Check(project));
        var config = File.ReadAllText(Path.Combine(project, "lizarddocs.yml"));
        Reject(() => SiteInitializer.Create(project, "Other")); Assert(config == File.ReadAllText(Path.Combine(project, "lizarddocs.yml")));
        Reject(() => SiteInitializer.Create(Path.Combine(temporary, "invalid"), "bad\nname"));
    });
    Test("build embeds all assets and preserves named routes anchors code and search", () => {
        Pass(SiteBuilder.Build(project)); Pass(SiteChecks.Check(output));
        Assert(File.Exists(Path.Combine(output, "guide/getting-started.html")));
        Assert(!File.Exists(Path.Combine(output, "guide/getting-started/index.html")));
        foreach (var asset in ThemeAssets.Files) Assert(File.ReadAllText(Path.Combine(output, ThemeAssets.DirectoryName, asset)) == ThemeAssets.Read(asset));
        var parser = new HtmlParser(); var home = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "index.html")));
        Assert(home.QuerySelector("h1")!.TextContent == "Atlas: API & Docs");
        Assert(!home.DocumentElement.OuterHtml.Contains("DirectSharp", StringComparison.Ordinal));
        var source = new MarkdownPage(File.ReadAllText(Path.Combine(project, "docs/guide/getting-started.md")));
        var page = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "guide/getting-started.html")));
        Assert(page.QuerySelector("pre code")!.TextContent == source.Fences[0].Body + "\n");
        foreach (var anchor in source.Anchors) Assert(page.GetElementById(anchor) is not null);
        var json = File.ReadAllText(Path.Combine(output, "search/search_index.json"));
        Assert(File.ReadAllText(Path.Combine(output, "search/search_index.js")) == "globalThis.lizardDocsSearchIndex = " + json + ";\n");
        var docs = JsonNode.Parse(json)!["docs"]!.AsArray(); Assert(docs.Count == 2 && docs.Any(d => d!["headings"]!.AsArray().Count > 0));
        Assert(!File.Exists(Path.Combine(output, "sitemap.xml")));
    });
    Test("branding source directories custom assets cards and metadata are per project", () => {
        var other = Path.Combine(temporary, "research"); SiteInitializer.Create(other, "Research Notes");
        Directory.Move(Path.Combine(other, "docs"), Path.Combine(other, "manual"));
        Directory.CreateDirectory(Path.Combine(other, "manual/assets"));
        File.WriteAllText(Path.Combine(other, "manual/assets/logo.svg"), "<svg xmlns='http://www.w3.org/2000/svg'><path d='M0 0'/></svg>");
        File.WriteAllText(Path.Combine(other, "manual/assets/custom.css"), "article { max-width: 70ch; }");
        var configPath = Path.Combine(other, "lizarddocs.yml");
        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("docs_dir: docs", "docs_dir: manual").Replace("brand_label: Docs", "brand_label: Notebook") + "brand_mark: RN\nlogo: assets/logo.svg\nsite_url: https://example.org/research/\nrepo_url: https://github.com/example/research\nedit_uri: edit/main/manual/\nfooter_links:\n  - Organisation: https://example.org/\nextra_css:\n  - assets/custom.css\nhome_cards:\n  - title: Explore\n    description: The analysis guide\n    page: guide/getting-started.md\n");
        // Replace, rather than duplicate, the initial theme key.
        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("#0f766e", "#6d3ca0"));
        File.AppendAllText(Path.Combine(other, "manual/index.md"), "\n![Logo](assets/logo.svg)\n");
        Pass(SiteBuilder.Build(other));
        var page = new HtmlParser().ParseDocument(File.ReadAllText(Path.Combine(other, "site/index.html")));
        Assert(page.QuerySelector(".brand-label")!.TextContent == "Notebook");
        Assert(page.QuerySelector(".brand-logo") is not null && page.QuerySelectorAll(".site-start-grid a").Length == 1);
        Assert(page.QuerySelector("meta[name='theme-color']")!.GetAttribute("content") == "#6d3ca0");
        Assert(page.QuerySelector("link[rel='canonical']")!.GetAttribute("href") == "https://example.org/research/");
        Assert(page.QuerySelector("a[data-source-link]")!.GetAttribute("href")!.EndsWith("blob/main/manual/index.md", StringComparison.Ordinal));
        Assert(File.Exists(Path.Combine(other, "site/assets/logo.svg")) && File.Exists(Path.Combine(other, "site/assets/custom.css")));
        Assert(File.ReadAllText(Path.Combine(output, "index.html")).Contains("Atlas: API &amp; Docs", StringComparison.Ordinal));
    });
    Test("general Markdown accepts arbitrary code languages and design topics without language policies", () => {
        var generic = new MarkdownPage("# Design\n\n## TODO for owner approval\n\n```fsharp\nlet value = 42\n```\n");
        Assert(generic.Errors.Count == 0);
        var path = Path.Combine(project, "docs/draft.md"); File.WriteAllText(path, "# Unlisted private draft\n");
        Pass(SiteBuilder.Build(project)); Assert(!File.Exists(Path.Combine(output, "draft.html")));
        Assert(!File.ReadAllText(Path.Combine(output, "search/search_index.json")).Contains("Unlisted private", StringComparison.Ordinal));
        File.Delete(path);
    });
    Test("source checks reject missing links anchors assets unpublished pages and traversal", () => {
        var path = Path.Combine(project, "docs/index.md"); var original = File.ReadAllText(path);
        try {
            foreach (var addition in new[] { "[x](missing.md)", "[x](guide/getting-started.md#missing)", "![x](assets/missing.png)", "[x](%2e%2e/lizarddocs.yml)", "[x](/root-relative.html)" }) {
                File.WriteAllText(path, original + "\n" + addition + "\n"); Assert(DocumentationChecks.Check(project).Errors.Count > 0, addition);
            }
        } finally { File.WriteAllText(path, original); }
    });
    Test("headings stable IDs duplicate headings and source fences survive rendering", () => {
        var page = new MarkdownPage("# Title\n\n## Résumé & `code`\n\n## Résumé & `code`\n\n<a id='explicit'></a>\n\n```other\n# not a heading\n```\n");
        Assert(page.Anchors.Contains("resume-code") && page.Anchors.Contains("resume-code_1") && page.Anchors.Contains("explicit"));
        Assert(page.Fences.Count == 1 && page.Fences[0].Body == "# not a heading");
        Assert(new MarkdownPage("```text\nunclosed").Errors.Count > 0);
    });
    Test("packaging is deterministic and contains the complete flat-root static site", () => {
        Pass(SitePackage.Create(project)); var archive = Path.Combine(project, "artifacts/lizarddocs-site.zip"); var first = Hash(archive);
        Pass(SitePackage.Create(project)); Assert(first == Hash(archive));
        using var zip = ZipFile.OpenRead(archive);
        Assert(zip.GetEntry("index.html") is not null && zip.GetEntry("guide/getting-started.html") is not null && zip.GetEntry("search/search_index.js") is not null);
        Assert(zip.Entries.All(entry => !entry.FullName.EndsWith(".dll", StringComparison.Ordinal)));
        var extracted = Path.Combine(temporary, "extracted"); ZipFile.ExtractToDirectory(archive, extracted); Pass(SiteChecks.Check(extracted));
    });
    Test("failed builds preserve prior site and ZIP bytes", () => {
        var path = Path.Combine(project, "docs/index.md"); var original = File.ReadAllText(path);
        var before = Hash(Path.Combine(output, "index.html")); var archive = Path.Combine(project, "artifacts/lizarddocs-site.zip"); var zipHash = Hash(archive);
        try {
            File.AppendAllText(path, "\n[Missing](missing.md)\n"); Assert(SitePackage.Create(project).Errors.Count > 0);
            Assert(before == Hash(Path.Combine(output, "index.html")) && zipHash == Hash(archive));
        } finally { File.WriteAllText(path, original); }
    });
    Test("project-specific output validators run before publication and preserve previous output", () => {
        var before = Hash(Path.Combine(output, "index.html")); var called = false;
        var result = SiteBuilder.Build(project, outputValidator: stage => { called = File.Exists(Path.Combine(stage, "index.html")); return new("Project policy", new[] { "Project-specific failure" }); });
        Assert(called && result.Errors.Contains("Project-specific failure"));
        Assert(before == Hash(Path.Combine(output, "index.html")), "A failed project-specific check replaced published output");
    });
    Test("output and archive protection refuse source overlap unowned directories and ZIPs", () => {
        foreach (var directory in new[] { project, Path.Combine(project, "docs/generated"), Path.Combine(project, "src/generated"), Path.Combine(project, ".git/generated") }) Reject(() => SiteBuilder.Build(project, directory));
        var unowned = Path.Combine(temporary, "unowned"); Directory.CreateDirectory(unowned); File.WriteAllText(Path.Combine(unowned, "work.txt"), "keep");
        Reject(() => SiteBuilder.Build(project, unowned)); Assert(File.ReadAllText(Path.Combine(unowned, "work.txt")) == "keep");
        Reject(() => SitePackage.Create(project, archive: Path.Combine(project, "docs/archive.zip")));
        var archive = Path.Combine(temporary, "unowned.zip"); using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { zip.CreateEntry("user-file.txt"); }
        var hash = Hash(archive); Reject(() => SitePackage.Create(project, archive: archive)); Assert(hash == Hash(archive));
    });
    Test("explicit legacy ownership migrates only matching generated output", () => {
        var legacy = Path.Combine(temporary, "legacy"); SiteInitializer.Create(legacy, "Legacy");
        File.AppendAllText(Path.Combine(legacy, "lizarddocs.yml"), "legacy_marker: .old-generator.json\nlegacy_generator: OldGenerator\n");
        var site = Path.Combine(legacy, "site"); Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, ".old-generator.json"), "{\"generator\":\"SomeoneElse\"}");
        Reject(() => SiteBuilder.Build(legacy));
        File.WriteAllText(Path.Combine(site, ".old-generator.json"), "{\"generator\":\"OldGenerator\"}");
        Pass(SiteBuilder.Build(legacy)); Assert(File.Exists(Path.Combine(site, SiteBuilder.Marker)) && !File.Exists(Path.Combine(site, ".old-generator.json")));
    });
    Test("config refuses unsafe URL colour and source-directory values", () => {
        var path = Path.Combine(project, "lizarddocs.yml"); var original = File.ReadAllText(path);
        try {
            foreach (var addition in new[] { "logo: javascript:alert(1)", "repo_url: https://name:password@example.org/", "site_url: https://example.org/?q=x", "legacy_marker: ../owned.json\nlegacy_generator: Other" }) {
                File.WriteAllText(path, original + "\n" + addition + "\n"); Reject(() => SiteConfiguration.Load(project));
            }
            File.WriteAllText(path, original.Replace("#0f766e", "red;display:none")); Reject(() => SiteConfiguration.Load(project));
            File.WriteAllText(path, original.Replace("docs_dir: docs", "docs_dir: ../other")); Reject(() => SiteConfiguration.Load(project));
        } finally { File.WriteAllText(path, original); }
    });
    Test("source symbolic links cannot publish files outside the project", () => {
        var outside = Path.Combine(temporary, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "index.md"), "# Secret\n");
        var link = Path.Combine(project, "escape"); Directory.CreateSymbolicLink(link, outside);
        var path = Path.Combine(project, "lizarddocs.yml"); var original = File.ReadAllText(path);
        try { File.WriteAllText(path, original.Replace("docs_dir: docs", "docs_dir: escape")); Reject(() => DocumentationChecks.Check(project)); }
        finally { File.WriteAllText(path, original); Directory.Delete(link); }
    });
    Test("presentation tokenization preserves source characters without parsing a language", () => {
        var engine = new Engine(); engine.Execute("var module={exports:{}};"); engine.Execute(ThemeAssets.Read("site.js"));
        const string source = "public class Example { string Name = \"<img src=x>\"; // comment\nreturn 42; }";
        engine.SetValue("source", source);
        var tokens = JsonNode.Parse(engine.Evaluate("JSON.stringify(module.exports.highlightTokens(source))").AsString())!.AsArray();
        Assert(string.Concat(tokens.Select(token => token!["text"]!.GetValue<string>())) == source);
        Assert(tokens.Any(token => token!["kind"]?.GetValue<string>() == "string") && tokens.Any(token => token!["kind"]?.GetValue<string>() == "comment"));
    });
    Test("loopback preview serves HTML search and 404 without a production application", () => {
        var app = PreviewServer.Create(output, 0);
        try {
            app.StartAsync().GetAwaiter().GetResult(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var path in new[] { "/", "/guide/getting-started.html", "/assets/lizarddocs/search.js", "/search/search_index.js" }) Assert(client.GetAsync(path).GetAwaiter().GetResult().StatusCode == HttpStatusCode.OK, path);
            Assert(client.GetAsync("/missing").GetAwaiter().GetResult().StatusCode == HttpStatusCode.NotFound);
        } finally { app.StopAsync().GetAwaiter().GetResult(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });
    Test("CLI exit codes distinguish success from bad commands and failed validation", () => {
        var stdout = Console.Out; var stderr = Console.Error;
        try {
            Console.SetOut(new StringWriter()); Console.SetError(new StringWriter());
            Assert(Cli.Main(["check", "--root", project]) == 0);
            Assert(Cli.Main(["unknown", "--root", project]) == 1);
            Assert(Cli.Main(["check", "--root", project, "--archive", "x.zip"]) == 1);
            Assert(Cli.Main(["new", project, "--name", "Overwrite"]) == 1);
        } finally { Console.SetOut(stdout); Console.SetError(stderr); }
    });
}
finally { Directory.Delete(temporary, recursive: true); }
Console.WriteLine($"LizardDocs tests: {passed} passed, {failed} failed."); return failed == 0 ? 0 : 1;
