using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LizardDocs;

public sealed record CodeFence(int Line, string Language, string Body, string Heading)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Body))).ToLowerInvariant();
}

public sealed class MarkdownPage
{
    public string Source { get; }
    public string Prose { get; }
    public string Html { get; }
    public IReadOnlyList<CodeFence> Fences { get; }
    public IReadOnlySet<string> Anchors { get; }
    public IReadOnlyList<string> Errors { get; }
    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal)
    { "directsharp", "csharp", "vbnet", "vb", "python", "go", "rust", "swift", "cpp", "java", "javascript", "typescript", "text", "plaintext", "sh", "bash", "powershell", "xml", "json", "yaml", "ebnf", "sql", "html", "" };

    public MarkdownPage(string source)
    {
        Source = source.ReplaceLineEndings("\n");
        var prose = new List<string>(); var fences = new List<CodeFence>(); var errors = new List<string>();
        string? active = null; var language = ""; var heading = ""; var fenceHeading = ""; var start = 0;
        var body = new List<string>(); int? tableWidth = null;
        var lines = Source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i]; var marker = Regex.Match(line, @"^\s*(`{3,}|~{3,})(.*)$");
            if (marker.Success)
            {
                var token = marker.Groups[1].Value; var rest = marker.Groups[2].Value.Trim();
                if (active is null)
                {
                    active = token; language = rest.Split(' ', 2)[0]; body.Clear(); start = i + 1; fenceHeading = heading;
                }
                else if (token[0] == active[0] && token.Length >= active.Length && rest.Length == 0)
                { fences.Add(new(start, language, string.Join('\n', body), fenceHeading)); active = null; }
                else body.Add(line);
                continue;
            }
            if (active is not null) { body.Add(line); continue; }
            prose.Add(line);
            if (Regex.IsMatch(line, @"^#{1,6} ")) heading = line.TrimStart('#').Trim();
            if (line.StartsWith('|'))
            {
                var width = Regex.Split(line, @"(?<!\\)\|").Length - 1;
                if (tableWidth is not null && width != tableWidth) errors.Add($"{i + 1}: inconsistent table columns");
                tableWidth = width;
            }
            else tableWidth = null;
        }
        if (active is not null) errors.Add($"{start}: unclosed fence");
        Prose = string.Join('\n', prose); Fences = fences; Errors = errors;

        // Preserve the published Markdown heading URLs, including Python-Markdown's
        // ASCII normalization and _1 duplicate suffix, independently of the site backend.
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseGenericAttributes().Build();
        var document = Markdown.Parse(Source, pipeline);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in document.Descendants<HeadingBlock>())
        {
            var attributes = block.GetAttributes();
            var id = attributes.Id ?? Slug(InlineText(block.Inline));
            var candidate = id; var number = 0;
            while (!used.Add(candidate)) candidate = id + "_" + ++number;
            attributes.Id = candidate;
        }
        Html = Markdown.ToHtml(document, pipeline);
        Anchors = Regex.Matches(Html, "\\bid=[\"']([^\"']+)").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var text = new StringBuilder();
        foreach (var child in inline)
            switch (child)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case HtmlEntityInline entity: text.Append(entity.Transcoded); break;
                case LineBreakInline: text.Append(' '); break;
                case ContainerInline container: text.Append(InlineText(container)); break;
            }
        return text.ToString();
    }
    private static string Slug(string text)
    {
        var ascii = new string(text.Normalize(NormalizationForm.FormKD).Where(c => c < 128).ToArray());
        ascii = Regex.Replace(ascii, @"[^\w\s-]", "").Trim().ToLowerInvariant();
        return Regex.Replace(ascii, @"[-\s]+", "-");
    }
    public static IEnumerable<string> Links(string prose) => Regex.Matches(prose, "(?<!!)\\[[^\\]]+\\]\\(([^\\s)]+)(?:\\s+\"[^\"]*\")?\\)").Select(m => m.Groups[1].Value);
}
