using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using LizardDocs;

internal static class SearchTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        Engine Create(bool dom = false)
        {
            var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(10)));
            engine.Execute("var module = { exports: {} };");
            if (dom) engine.Execute("""
                var document = {
                    querySelector: () => null,
                    createTextNode: text => ({ kind: 'text', textContent: text }),
                    createElement: tag => ({ kind: tag, textContent: '' })
                };
                var target = { children: [], append(child) { this.children.push(child); } };
                Object.defineProperty(target, 'innerHTML', { set() { throw Error('Unsafe HTML insertion'); } });
                """);
            engine.Execute(ThemeAssets.Read("search.js"));
            return engine;
        }
        JsonNode Evaluate(Engine engine, string expression) => JsonNode.Parse(engine.Evaluate("JSON.stringify(" + expression + ")").AsString())!;
        string[] Locations(object[] pages, string query)
        {
            var engine = Create(); engine.SetValue("pagesJson", JsonSerializer.Serialize(pages)); engine.SetValue("query", query);
            return Evaluate(engine, "module.exports.rankPages(JSON.parse(pagesJson), query).map(match => match.page.location)")
                .AsArray().Select(node => node!.GetValue<string>()).ToArray();
        }
        object Page(string location, string title, string text, params string[] headings) => new
        {
            location, title, text,
            headings = headings.Select((heading, index) => new { text = heading, location = location + "#section-" + index }).ToArray()
        };
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }

        test("search ranks titles before headings before body regardless of repetition", () =>
        {
            var pages = new[] {
                Page("body.html", "Overview", string.Concat(Enumerable.Repeat("consume ", 100))),
                Page("heading.html", "Collections", "Forward-only sources", "consume: acquire pending work"),
                Page("title.html", "Consume", "Acquire work")
            };
            Assert(Locations(pages, "CONSUME").SequenceEqual(new[] { "title.html", "heading.html", "body.html" }), "Search field priority changed");
        });
        test("search requires all terms and ranks title coverage with deterministic ties", () =>
        {
            var pages = new[] {
                Page("heading.html", "Processing", "", "Concurrent loops"),
                Page("partial-title.html", "Concurrent work", "loops"),
                Page("title-a.html", "Concurrent loops", ""),
                Page("title-b.html", "Concurrent loops", ""),
                Page("missing.html", "Concurrent", "unrelated")
            };
            Assert(Locations(pages, " concurrent   LOOPS ").SequenceEqual(new[] { "title-a.html", "title-b.html", "partial-title.html", "heading.html" }), "Multi-term ranking or tie order changed");
            Assert(Locations(pages, "  ").Length == 0, "Empty query returned pages");
            Assert(Locations(pages, "missing word").Length == 0, "An unmatched term was ignored");
        });
        test("search selects a matching heading and keeps its original fragment", () =>
        {
            var engine = Create();
            engine.SetValue("pagesJson", JsonSerializer.Serialize(new[] { Page("loops.html", "Control flow", "", "while", "concurrent consume", "consume") }));
            var match = Evaluate(engine, "module.exports.rankPages(JSON.parse(pagesJson), 'consume')[0]");
            Assert(match["heading"]!["text"]!.GetValue<string>() == "consume", "Exact heading was not preferred");
            Assert(match["heading"]!["location"]!.GetValue<string>() == "loops.html#section-2", "Section fragment was changed");
        });
        test("search prefers whole keywords over fragments while retaining partial queries", () =>
        {
            var pages = new[] {
                Page("fragment.html", "Callables", "The smallest allocation"),
                Page("body.html", "Other operations", "Wait for all results"),
                Page("heading.html", "Concurrent work", "", "concurrent all"),
                Page("title.html", "All results", "")
            };
            Assert(Locations(pages, "all").SequenceEqual(new[] { "title.html", "heading.html", "body.html", "fragment.html" }), "A fragment outranked a whole keyword");
            Assert(Locations(pages, "callab").SequenceEqual(new[] { "fragment.html" }), "Partial typing stopped finding results");
            Assert(Locations(new[] { Page("loop.html", "for_each", "") }, "for").Length == 1, "Partial keyword query was lost");
        });
        test("search highlighting handles case, punctuation, repeated and overlapping terms", () =>
        {
            var engine = Create();
            foreach (var (text, query, expected) in new[] {
                ("Concurrent ALL and all", "all", new[] { "ALL", "all" }),
                ("task_of(x) ?? fallback + C++", "?? C++", new[] { "??", "C++" }),
                ("a.b aXb [x]", ". [x]", new[] { ".", "[x]" }),
                ("concurrent", "con concurrent con", new[] { "concurrent" }),
                ("CAFÉ résumé", "café", new[] { "CAFÉ" }),
                ("Nothing matches", " ", Array.Empty<string>())
            })
            {
                engine.SetValue("text", text); engine.SetValue("query", query);
                var parts = Evaluate(engine, "module.exports.highlightParts(text, query)").AsArray();
                Assert(string.Concat(parts.Select(part => part!["text"]!.GetValue<string>())) == text, "Highlighting changed source text");
                Assert(parts.Where(part => part!["match"]!.GetValue<bool>()).Select(part => part!["text"]!.GetValue<string>()).SequenceEqual(expected), "Incorrect literal highlight: " + query);
                Assert(parts.All(part => !part!["match"]!.GetValue<bool>() || part["text"]!.GetValue<string>().Length > 0), "Empty highlighted span");
            }
        });
        test("search rendering treats markup as text and creates only mark elements", () =>
        {
            var engine = Create(dom: true);
            const string text = "<img src=x onerror=alert(1)> Consume & consume";
            engine.SetValue("text", text);
            engine.Execute("module.exports.appendHighlighted(target, text, 'consume <img');");
            var children = Evaluate(engine, "target.children").AsArray();
            Assert(string.Concat(children.Select(child => child!["textContent"]!.GetValue<string>())) == text, "Rendered text was changed");
            Assert(children.All(child => child!["kind"]!.GetValue<string>() is "text" or "mark"), "Query created HTML elements");
            Assert(children.Count(child => child!["kind"]!.GetValue<string>() == "mark") == 3, "Expected title/body highlight spans missing");
        });
        test("search excerpts show matching sections and avoid false truncation markers", () =>
        {
            var engine = Create();
            engine.SetValue("pageJson", JsonSerializer.Serialize(Page("page.html", "Title", new string('x', 220) + "\nConsume jobs\nAcquire pending work.")));
            var text = engine.Evaluate("module.exports.excerptFor(JSON.parse(pageJson), 'consume', {text:'Consume jobs'})").AsString();
            Assert(text == "…Consume jobs Acquire pending work.", "Excerpt did not start at the matching heading");
            engine.SetValue("pageJson", JsonSerializer.Serialize(Page("page.html", "Title", "A short body.")));
            Assert(engine.Evaluate("module.exports.excerptFor(JSON.parse(pageJson), 'absent', null)").AsString() == "A short body.", "Short excerpt gained a false ellipsis");
        });
    }
}
