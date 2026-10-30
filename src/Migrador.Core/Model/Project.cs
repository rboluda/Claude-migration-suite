using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrador.Core.Model;

public sealed class Project
{
    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public string? Description { get; init; }
    /// <summary>Custom instructions of the project.</summary>
    public string? Instructions { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public List<ProjectDocument> Documents { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A knowledge file of a project.</summary>
public sealed class ProjectDocument : ResolvableItem
{
    public string? Id { get; init; }
    public string? FileName { get; init; }
    public string? Content { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public enum Provenance
{
    Export,
    Local,
    Cloud,
    Manual,
}

/// <summary>A binary file, stored once and addressed by its SHA-256.</summary>
public sealed class Blob
{
    public required string Sha256 { get; init; }
    public string Extension { get; init; } = "";
    public long Size { get; init; }
    public List<BlobLocation> Locations { get; init; } = [];

    /// <summary>Path inside the .clo package.</summary>
    [JsonIgnore]
    public string EntryName => $"blobs/{Sha256}{Extension}";
}

/// <summary>Where a blob was found. The same hash on several machines is one blob with several locations.</summary>
public sealed class BlobLocation
{
    public Provenance Source { get; init; }
    public string? Machine { get; init; }
    public string? Path { get; init; }
    public DateTimeOffset FoundAt { get; init; }
}
