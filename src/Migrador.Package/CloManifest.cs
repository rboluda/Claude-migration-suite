using Migrador.Core.Model;

namespace Migrador.Package;

/// <summary>manifest.json: what the package holds and the SHA-256 of every other entry.</summary>
public sealed class CloManifest
{
    public const string FormatName = "clo";
    public const int CurrentSchemaVersion = 1;

    public string Format { get; init; } = FormatName;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset CreatedAt { get; init; }
    public string? AppVersion { get; init; }
    public AccountInfo? Account { get; init; }
    public ParseReport? Source { get; init; }
    public bool HasOriginalExport { get; init; }
    /// <summary>Machines whose data is in the package.</summary>
    public List<string> Machines { get; init; } = [];
    public List<string> Conversations { get; init; } = [];
    public List<string> Projects { get; init; } = [];
    public List<Blob> Blobs { get; init; } = [];
    /// <summary>Metadata of each reconstruction; the text is in reconstructions/&lt;id&gt;.md.</summary>
    public List<Reconstruction> Reconstructions { get; init; } = [];
    /// <summary>Entry name → SHA-256 of its content.</summary>
    public SortedDictionary<string, string> Hashes { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Entry names inside a .clo package.</summary>
public static class CloEntries
{
    public const string Manifest = "manifest.json";
    public const string OriginalExport = "original/export.zip";

    public static string Chat(string id) => $"chats/{Safe(id)}.json";
    public static string Project(string id) => $"projects/{Safe(id)}.json";
    public static string Reconstruction(string id) => $"reconstructions/{Safe(id)}.md";
    public static string ClaudeCodeSnapshot(string machine) => $"claude-code/{Safe(machine)}.json";
    public static string ClaudeCodeFile(string machine, string relativePath) => $"claude-code/{Safe(machine)}/{relativePath}";

    /// <summary>Ids and machine names as safe single path segments.</summary>
    public static string Safe(string name)
    {
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var s = new string(chars).Trim('.');
        return s.Length == 0 ? "_" : s;
    }
}
