using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrador.Core.Model;

/// <summary>How the message tree of a conversation was obtained.</summary>
public enum TreeSource
{
    /// <summary>Every message says who its parent is.</summary>
    Explicit,
    /// <summary>The export gave no parents: the messages are chained in export order. Branches may be missing.</summary>
    InferredLinear,
}

/// <summary>A chat. Its messages form a tree: edits and regenerations create branches.</summary>
public sealed class Conversation
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public string? Summary { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? ProjectId { get; init; }
    /// <summary>Leaf of the branch that was active in the account (the default branch on export).</summary>
    public string? CurrentLeafId { get; init; }
    public TreeSource TreeSource { get; init; }
    public List<Message> Messages { get; init; } = [];

    /// <summary>Fields of the export this version of the parser does not interpret, kept as they were.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public enum MessageRole
{
    Unknown,
    Human,
    Assistant,
}

public sealed class Message
{
    public required string Id { get; init; }
    public string? ParentId { get; init; }
    public MessageRole Role { get; init; }
    /// <summary>The role exactly as the export wrote it.</summary>
    public string? RawRole { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    /// <summary>Plain text of the message as the export gives it.</summary>
    public string Text { get; init; } = "";
    /// <summary>Structured content blocks (text, thinking, tool calls…), each one with its original JSON.</summary>
    public List<ContentBlock> Content { get; init; } = [];
    /// <summary>Files whose text the export includes.</summary>
    public List<Attachment> Attachments { get; init; } = [];
    /// <summary>Files (mostly images) the export only references.</summary>
    public List<FileReference> Files { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ContentBlock
{
    public string Type { get; init; } = "";
    /// <summary>Readable text of the block, if it has any.</summary>
    public string? Text { get; init; }
    /// <summary>The whole block exactly as it was in the export.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>An item that may need resolving (export → local → cloud → user).</summary>
public abstract class ResolvableItem
{
    public Fidelity Fidelity { get; set; }
    public ItemState State { get; set; }
    /// <summary>Content-addressed blob holding the file, once found.</summary>
    public string? BlobSha256 { get; set; }
}

public sealed class Attachment : ResolvableItem
{
    public string? FileName { get; init; }
    public string? FileType { get; init; }
    public long? FileSize { get; init; }
    public string? ExtractedText { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class FileReference : ResolvableItem
{
    public string? FileName { get; init; }
    public string? FileId { get; init; }
    public string? Kind { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
