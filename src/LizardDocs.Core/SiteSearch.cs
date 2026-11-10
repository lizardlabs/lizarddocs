namespace LizardDocs;

internal static class SiteSearch
{
    internal const string JsonPath = "search/search_index.json";
    internal const string ScriptPath = "search/search_index.js";

    // A classic script can load beside file:// pages, where fetch(JSON) is blocked.
    // Use the same serialized public index for offline and hosted search.
    internal static string Script(string json) => "globalThis.lizardDocsSearchIndex = " + json + ";\n";
}
