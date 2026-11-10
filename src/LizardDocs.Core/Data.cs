using System.Text.Json.Nodes;

namespace LizardDocs;

internal static class Data
{
    public static string Read(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path); var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)?.FullName ?? throw new InvalidDataException("Unresolved link: " + path);
        }
        return current;
    }
    public static bool Within(string root, string path) => path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    public static JsonObject LoadObject(string path) => JsonNode.Parse(Read(path)) as JsonObject ?? throw new InvalidDataException("Expected object: " + path);
    public static string String(JsonNode? node, string label, bool empty = false) => node is JsonValue value && value.TryGetValue<string>(out var text) && (empty || text.Length > 0) ? text : throw new InvalidDataException($"{label}: expected string");
    public static string Text(JsonObject item, string name, bool empty = false) => String(item[name], name, empty);
    public static JsonArray Array(JsonObject item, string name) => item[name] as JsonArray ?? throw new InvalidDataException($"{name}: expected array");
}

public sealed record CheckResult(string Summary, IReadOnlyList<string> Errors);
