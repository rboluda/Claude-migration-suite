using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Migrador.Core;
using Migrador.Core.Model;

namespace Migrador.Parsers.Export;

/// <summary>
/// Reads the data export ZIP of claude.ai. The format is not a stable contract: known fields are interpreted, every
/// other field is kept as is in <c>Extra</c>, and ZIP entries that are not understood are listed in the report (they
/// are still inside the original export, which is always kept).
/// </summary>
/// <remarks>
/// PROVISIONAL until M0: the field names come from the publicly known layout of the export and must be checked against
/// a real export (docs/export-schema.md).
/// </remarks>
public static class ExportParser
{
    public const string Version = "0.1-provisional";

    /// <summary>Parent id the service uses for the first message of a conversation.</summary>
    private const string RootParent = "00000000-0000-4000-8000-000000000000";

    public static async Task<Archive> ParseAsync(string zipPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(zipPath);
        var report = new ParseReport
        {
            ParserVersion = Version,
            ExportFileName = Path.GetFileName(full),
            ExportSha256 = await HashFileAsync(full, ct),
            ParsedAt = DateTimeOffset.Now,
        };
        var archive = new Archive { Report = report, Content = new ExportContent(full) };

        using var zip = ZipFile.OpenRead(full);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/'))
                continue;
            switch (entry.FullName.ToLowerInvariant())
            {
                case "conversations.json":
                    progress?.Report(entry.FullName);
                    await using (var s = entry.Open())
                        await foreach (var e in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(s, cancellationToken: ct))
                            if (e.ValueKind == JsonValueKind.Object)
                                archive.Conversations.Add(ReadConversation(e, report));
                            else
                                report.Warnings.Add($"conversations.json: unexpected {e.ValueKind} element skipped.");
                    break;
                case "projects.json":
                    progress?.Report(entry.FullName);
                    await using (var s = entry.Open())
                        await foreach (var e in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(s, cancellationToken: ct))
                            if (e.ValueKind == JsonValueKind.Object)
                                archive.Projects.Add(ReadProject(e, report));
                    break;
                case "users.json":
                    await using (var s = entry.Open())
                        await foreach (var e in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(s, cancellationToken: ct))
                            if (e.ValueKind == JsonValueKind.Object && archive.Account is null)
                                archive.Account = new AccountInfo
                                {
                                    Id = Str(e, "uuid"),
                                    Name = Str(e, "full_name"),
                                    Email = Str(e, "email_address"),
                                    Extra = Rest(e, "uuid", "full_name", "email_address"),
                                };
                    break;
                default:
                    report.UnknownEntries.Add(entry.FullName);
                    break;
            }
        }

        if (archive.Conversations.Count == 0)
            report.Warnings.Add("The export has no conversations.json, or it is empty.");
        return archive;
    }

    internal static Conversation ReadConversation(JsonElement e, ParseReport report)
    {
        var id = Str(e, "uuid") ?? Guid.NewGuid().ToString();
        var messages = new List<Message>();
        var anyParent = false;
        if (e.TryGetProperty("chat_messages", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var m in list.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object)
                    continue;
                anyParent |= m.TryGetProperty("parent_message_uuid", out _);
                messages.Add(ReadMessage(m, id, report));
            }

        var source = anyParent ? TreeSource.Explicit : TreeSource.InferredLinear;
        if (!anyParent && messages.Count > 1)
        {
            // No parents in the export: chain the messages in the order they come. Any branches are not in the data.
            for (var i = 1; i < messages.Count; i++)
                messages[i] = WithParent(messages[i], messages[i - 1].Id);
        }

        return new Conversation
        {
            Id = id,
            Title = Str(e, "name") ?? "",
            Summary = NullIfEmpty(Str(e, "summary")),
            CreatedAt = Date(e, "created_at", id, report),
            UpdatedAt = Date(e, "updated_at", id, report),
            ProjectId = Str(e, "project_uuid"),
            CurrentLeafId = Str(e, "current_leaf_message_uuid"),
            TreeSource = source,
            Messages = messages,
            Extra = Rest(e, "uuid", "name", "summary", "created_at", "updated_at", "project_uuid", "current_leaf_message_uuid", "chat_messages"),
        };
    }

    private static Message ReadMessage(JsonElement m, string conversationId, ParseReport report)
    {
        var sender = Str(m, "sender");
        var parent = Str(m, "parent_message_uuid");
        var content = new List<ContentBlock>();
        if (m.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            foreach (var b in blocks.EnumerateArray())
                content.Add(new ContentBlock
                {
                    Type = Str(b, "type") ?? "",
                    Text = Str(b, "text") ?? Str(b, "thinking"),
                    Raw = b.Clone(),
                });

        var attachments = new List<Attachment>();
        if (m.TryGetProperty("attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
            foreach (var a in atts.EnumerateArray())
            {
                var name = Str(a, "file_name");
                var text = Str(a, "extracted_content");
                attachments.Add(new Attachment
                {
                    FileName = name,
                    FileType = Str(a, "file_type"),
                    FileSize = a.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var n) ? n : null,
                    ExtractedText = text,
                    Fidelity = string.IsNullOrEmpty(text) ? Fidelity.Lost : FidelityRules.ForExtractedText(name),
                    Extra = Rest(a, "file_name", "file_type", "file_size", "extracted_content"),
                });
            }

        var files = new List<FileReference>();
        if (m.TryGetProperty("files", out var fs) && fs.ValueKind == JsonValueKind.Array)
            foreach (var f in fs.EnumerateArray())
                files.Add(new FileReference
                {
                    FileName = Str(f, "file_name"),
                    FileId = Str(f, "file_uuid"),
                    Kind = Str(f, "file_kind"),
                    // The export only references these files: until the original is found, only the reference remains.
                    Fidelity = Fidelity.Lost,
                    Extra = Rest(f, "file_name", "file_uuid", "file_kind"),
                });

        var id = Str(m, "uuid") ?? Guid.NewGuid().ToString();
        return new Message
        {
            Id = id,
            ParentId = parent is null or RootParent ? null : parent,
            Role = sender switch
            {
                "human" => MessageRole.Human,
                "assistant" => MessageRole.Assistant,
                _ => MessageRole.Unknown,
            },
            RawRole = sender,
            CreatedAt = Date(m, "created_at", $"{conversationId}/{id}", report),
            UpdatedAt = Date(m, "updated_at", $"{conversationId}/{id}", report),
            Text = Str(m, "text") ?? string.Concat(content.Where(c => c.Type == "text").Select(c => c.Text)),
            Content = content,
            Attachments = attachments,
            Files = files,
            Extra = Rest(m, "uuid", "parent_message_uuid", "sender", "created_at", "updated_at", "text", "content", "attachments", "files"),
        };
    }

    internal static Project ReadProject(JsonElement e, ParseReport report)
    {
        var id = Str(e, "uuid") ?? Guid.NewGuid().ToString();
        var docs = new List<ProjectDocument>();
        if (e.TryGetProperty("docs", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var d in list.EnumerateArray())
            {
                var name = Str(d, "filename");
                var content = Str(d, "content");
                docs.Add(new ProjectDocument
                {
                    Id = Str(d, "uuid"),
                    FileName = name,
                    Content = content,
                    CreatedAt = Date(d, "created_at", id, report),
                    Fidelity = string.IsNullOrEmpty(content) ? Fidelity.Lost : FidelityRules.ForExtractedText(name),
                    Extra = Rest(d, "uuid", "filename", "content", "created_at"),
                });
            }

        return new Project
        {
            Id = id,
            Name = Str(e, "name") ?? "",
            Description = NullIfEmpty(Str(e, "description")),
            Instructions = NullIfEmpty(Str(e, "prompt_template")),
            CreatedAt = Date(e, "created_at", id, report),
            UpdatedAt = Date(e, "updated_at", id, report),
            Documents = docs,
            Extra = Rest(e, "uuid", "name", "description", "prompt_template", "created_at", "updated_at", "docs"),
        };
    }

    private static Message WithParent(Message m, string parent) => new()
    {
        Id = m.Id, ParentId = parent, Role = m.Role, RawRole = m.RawRole, CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt,
        Text = m.Text, Content = m.Content, Attachments = m.Attachments, Files = m.Files, Extra = m.Extra,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static DateTimeOffset? Date(JsonElement e, string name, string where, ParseReport report)
    {
        if (Str(e, name) is not { } s)
            return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            return d;
        report.Warnings.Add($"{where}: unreadable date in {name}: \"{s}\".");
        return null;
    }

    /// <summary>Every property not in <paramref name="known"/>, kept as it was. Null if there are none.</summary>
    private static Dictionary<string, JsonElement>? Rest(JsonElement e, params string[] known)
    {
        Dictionary<string, JsonElement>? rest = null;
        foreach (var p in e.EnumerateObject())
            if (Array.IndexOf(known, p.Name) < 0)
                (rest ??= new Dictionary<string, JsonElement>())[p.Name] = p.Value.Clone();
        return rest;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
    }
}

/// <summary>Content of an archive that has just been read from an export: only the export itself.</summary>
public sealed class ExportContent(string exportPath) : IArchiveContent
{
    public Stream? OpenOriginalExport() => File.OpenRead(exportPath);

    public Stream? OpenBlob(Blob blob) => null;

    public Stream? OpenClaudeCodeFile(ClaudeCodeSnapshot snapshot, ClaudeCodeFile file) => null;
}
