namespace Migrador.Core.Model;

/// <summary>Copy of a machine's ~/.claude folder (without credentials).</summary>
public sealed class ClaudeCodeSnapshot
{
    public required string Machine { get; init; }
    /// <summary>The ~/.claude folder it was taken from.</summary>
    public required string SourceRoot { get; init; }
    public DateTimeOffset TakenAt { get; init; }
    /// <summary>Value of cleanupPeriodDays in settings.json; null if not set (Claude Code then uses 30).</summary>
    public int? CleanupPeriodDays { get; init; }
    public List<ClaudeCodeFile> Files { get; init; } = [];
    public List<ClaudeCodeProject> Projects { get; init; } = [];
    /// <summary>Files deliberately left out (credentials, caches), so the user can see what is not copied.</summary>
    public List<string> Excluded { get; init; } = [];
}

public enum ClaudeCodeFileKind
{
    Other,
    Instructions,
    Settings,
    Skill,
    Agent,
    Command,
    Plugin,
    Transcript,
    Memory,
}

public sealed class ClaudeCodeFile
{
    /// <summary>Path relative to ~/.claude, with '/' separators.</summary>
    public required string RelativePath { get; init; }
    public ClaudeCodeFileKind Kind { get; init; }
    public long Size { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset LastWriteTime { get; init; }
}

/// <summary>A folder of ~/.claude/projects: the transcripts and memory of one working folder.</summary>
public sealed class ClaudeCodeProject
{
    /// <summary>Name of the folder under projects/ (the working folder path, encoded).</summary>
    public required string FolderName { get; init; }
    /// <summary>Working folder it belongs to, if it could be determined; null if unknown.</summary>
    public string? Path { get; init; }
    /// <summary>URL of the git remote: identifies the same project on different machines.</summary>
    public string? GitRemote { get; init; }
    public int SessionCount { get; init; }
    public bool HasMemory { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
}
