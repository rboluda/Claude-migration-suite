using System.Text.Json.Serialization;

namespace Migrador.Core.Model;

/// <summary>Everything preserved from one account: the normalized model behind a .clo package.</summary>
public sealed class Archive
{
    public AccountInfo? Account { get; set; }
    public List<Conversation> Conversations { get; init; } = [];
    public List<Project> Projects { get; init; } = [];
    public List<Blob> Blobs { get; init; } = [];
    public List<Reconstruction> Reconstructions { get; init; } = [];
    public List<ClaudeCodeSnapshot> ClaudeCode { get; init; } = [];
    public ParseReport? Report { get; set; }

    /// <summary>Where binary content (original export, blobs, Claude Code files) can be read from. Not serialized.</summary>
    [JsonIgnore]
    public IArchiveContent? Content { get; set; }
}

/// <summary>Access to the binary content of an archive, wherever it currently lives (export ZIP, .clo, disk).</summary>
public interface IArchiveContent
{
    /// <summary>The original export, untouched, or null if the archive has none.</summary>
    Stream? OpenOriginalExport();

    Stream? OpenBlob(Blob blob);

    Stream? OpenClaudeCodeFile(ClaudeCodeSnapshot snapshot, ClaudeCodeFile file);
}

public sealed class AccountInfo
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>What the parser recognized and what it did not. Unknown data is reported, never dropped silently.</summary>
public sealed class ParseReport
{
    public required string ParserVersion { get; init; }
    public string? ExportFileName { get; init; }
    public string? ExportSha256 { get; init; }
    public DateTimeOffset ParsedAt { get; init; }
    /// <summary>ZIP entries the parser does not understand. They are still inside the original export.</summary>
    public List<string> UnknownEntries { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>A description generated afterwards (for example of an image that could not be recovered).</summary>
public sealed class Reconstruction
{
    public required string Id { get; init; }
    public required string ConversationId { get; init; }
    public string? MessageId { get; init; }
    /// <summary>File name of the item it describes, as referenced in the conversation.</summary>
    public string? TargetName { get; init; }
    public required string Text { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>How it was obtained, e.g. "asked in the original chat".</summary>
    public string? Method { get; init; }
}
