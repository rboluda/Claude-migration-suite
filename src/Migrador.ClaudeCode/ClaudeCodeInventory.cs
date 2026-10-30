using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrador.Core.Model;

namespace Migrador.ClaudeCode;

/// <summary>
/// Inventory of a machine's ~/.claude folder: what can be preserved, and what is deliberately left out (credentials,
/// session tokens, caches that are downloaded again).
/// </summary>
public static partial class ClaudeCodeInventory
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>Claude Code deletes transcripts older than this unless cleanupPeriodDays says otherwise.</summary>
    public const int DefaultCleanupPeriodDays = 30;

    /// <summary>Top-level files never copied: they hold credentials or account state.</summary>
    private static readonly string[] ExcludedFiles =
    [
        ".credentials.json", ".claude.json", "policy-limits.json", "remote-settings.json",
    ];

    /// <summary>Top-level folders never copied: session tokens (ide), runtime state and caches.</summary>
    private static readonly string[] ExcludedFolders =
    [
        "ide", "backups", "session-env", "sessions", "shell-snapshots", "cache", "debug", "statsig",
        "plugins/cache", "plugins/marketplaces",
    ];

    public static ClaudeCodeSnapshot Take(string root, string machine, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        var files = new List<ClaudeCodeFile>();
        var excluded = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsExcluded(rel))
            {
                excluded.Add(rel);
                continue;
            }
            progress?.Report(rel);
            var info = new FileInfo(path);
            files.Add(new ClaudeCodeFile
            {
                RelativePath = rel,
                Kind = KindOf(rel),
                Size = info.Length,
                Sha256 = Hash(path),
                LastWriteTime = info.LastWriteTimeUtc,
            });
        }

        return new ClaudeCodeSnapshot
        {
            Machine = machine,
            SourceRoot = root,
            TakenAt = DateTimeOffset.Now,
            CleanupPeriodDays = ReadCleanupPeriod(Path.Combine(root, "settings.json")),
            Files = files,
            Projects = ReadProjects(Path.Combine(root, "projects"), ct),
            Excluded = excluded,
        };
    }

    public static bool IsExcluded(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        var name = Path.GetFileName(rel);
        // Also their companions: ".claude.json.backup", "policy-limits.json.signature.json"…
        if (!rel.Contains('/') && ExcludedFiles.Any(f => name.Equals(f, StringComparison.OrdinalIgnoreCase)
                                                         || name.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (ExcludedFolders.Any(d => rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
            return true;
        // Anything that looks like a secret, wherever it is.
        return name.Contains("credential", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
    }

    public static ClaudeCodeFileKind KindOf(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        var parts = rel.Split('/');
        if (parts.Length == 1)
            return rel.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase) ? ClaudeCodeFileKind.Instructions
                : rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && (rel.StartsWith("settings", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("keybindings", StringComparison.OrdinalIgnoreCase))
                    ? ClaudeCodeFileKind.Settings
                    : ClaudeCodeFileKind.Other;
        return parts[0].ToLowerInvariant() switch
        {
            "skills" => ClaudeCodeFileKind.Skill,
            "agents" => ClaudeCodeFileKind.Agent,
            "commands" => ClaudeCodeFileKind.Command,
            "plugins" => ClaudeCodeFileKind.Plugin,
            "projects" when parts.Length > 2 && parts[2].Equals("memory", StringComparison.OrdinalIgnoreCase) => ClaudeCodeFileKind.Memory,
            "projects" when rel.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) => ClaudeCodeFileKind.Transcript,
            _ => ClaudeCodeFileKind.Other,
        };
    }

    /// <summary>
    /// Name Claude Code gives the folder of a working folder under projects/: every character that is not an ASCII
    /// letter or digit becomes '-' ("C:\PROYECTOS\Migrador" → "C--PROYECTOS-Migrador"). Checked against real data
    /// (2026-10-30). It cannot be reversed, so the real path is taken from the transcripts.
    /// </summary>
    public static string EncodeProjectPath(string path) => NotAlphanumeric().Replace(path, "-");

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NotAlphanumeric();

    private static List<ClaudeCodeProject> ReadProjects(string projectsDir, CancellationToken ct)
    {
        var result = new List<ClaudeCodeProject>();
        if (!Directory.Exists(projectsDir))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(projectsDir))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            var transcripts = Directory.GetFiles(dir, "*.jsonl");
            var path = ResolvePath(name, transcripts);
            var times = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).ToList();
            result.Add(new ClaudeCodeProject
            {
                FolderName = name,
                Path = path,
                GitRemote = path is null ? null : GitRemote(path),
                SessionCount = transcripts.Length,
                HasMemory = Directory.Exists(Path.Combine(dir, "memory")),
                LastActivity = times.Count == 0 ? null : new DateTimeOffset(times.Max(), TimeSpan.Zero),
            });
        }
        return result;
    }

    /// <summary>
    /// The working folder of a projects/ folder: one of the "cwd" values in its transcripts whose encoding is the
    /// folder name. A session may start in one folder and move to another, so the first "cwd" is not enough.
    /// </summary>
    internal static string? ResolvePath(string folderName, IEnumerable<string> transcripts)
    {
        foreach (var file in transcripts)
            foreach (var cwd in CwdValues(file))
                if (EncodeProjectPath(cwd) == folderName)
                    return cwd;
        return null;
    }

    private static IEnumerable<string> CwdValues(string transcript, int maxLines = 2000)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        for (var i = 0; i < maxLines && reader.ReadLine() is { } line; i++)
        {
            if (!line.Contains("\"cwd\"", StringComparison.Ordinal))
                continue;
            string? cwd = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("cwd", out var v) && v.ValueKind == JsonValueKind.String)
                    cwd = v.GetString();
            }
            catch (JsonException)
            {
                // A line being written by an open session: skip it.
            }
            if (cwd is not null && seen.Add(cwd))
                yield return cwd;
        }
    }

    /// <summary>URL of the "origin" remote (or the first remote) in the folder's .git/config, if it exists.</summary>
    internal static string? GitRemote(string folder)
    {
        var config = Path.Combine(folder, ".git", "config");
        if (!File.Exists(config))
            return null;
        string? section = null, first = null;
        foreach (var raw in File.ReadLines(config))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
                section = line;
            else if (section is not null && section.StartsWith("[remote ", StringComparison.Ordinal) && line.StartsWith("url", StringComparison.Ordinal) && line.IndexOf('=') is var eq and > 0)
            {
                var url = line[(eq + 1)..].Trim();
                if (section == "[remote \"origin\"]")
                    return url;
                first ??= url;
            }
        }
        return first;
    }

    private static int? ReadCleanupPeriod(string settings)
    {
        try
        {
            if (!File.Exists(settings))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            return doc.RootElement.TryGetProperty("cleanupPeriodDays", out var v) && v.TryGetInt32(out var days) ? days : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}

/// <summary>Content of snapshots just taken on this machine: read from ~/.claude itself.</summary>
public sealed class LocalClaudeCodeContent(params IEnumerable<ClaudeCodeSnapshot> snapshots) : IArchiveContent
{
    private readonly HashSet<ClaudeCodeSnapshot> _snapshots = [.. snapshots];

    public Stream? OpenOriginalExport() => null;

    public Stream? OpenBlob(Blob blob) => null;

    public Stream? OpenClaudeCodeFile(ClaudeCodeSnapshot snapshot, ClaudeCodeFile file)
    {
        if (!_snapshots.Contains(snapshot))
            return null;
        var path = Path.Combine(snapshot.SourceRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete) : null;
    }
}
