using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Migrador.Core.Model;

namespace Migrador.Package;

/// <summary>Reads a .clo package into an <see cref="Archive"/>. Binary content stays in the package until opened.</summary>
public static class CloReader
{
    public static Archive Read(string path)
    {
        var full = Path.GetFullPath(path);
        using var zip = ZipFile.OpenRead(full);
        var manifest = ReadJson<CloManifest>(zip, CloEntries.Manifest)
            ?? throw new InvalidDataException("manifest.json is empty.");
        if (manifest.Format != CloManifest.FormatName)
            throw new InvalidDataException($"Not a .clo package (format \"{manifest.Format}\").");
        if (manifest.SchemaVersion > CloManifest.CurrentSchemaVersion)
            throw new NotSupportedException($"The package uses schema version {manifest.SchemaVersion}; this version of the app reads up to {CloManifest.CurrentSchemaVersion}.");

        var archive = new Archive
        {
            Account = manifest.Account,
            Report = manifest.Source,
            Blobs = manifest.Blobs,
            Content = new CloContent(full, manifest.HasOriginalExport),
        };
        foreach (var id in manifest.Conversations)
            archive.Conversations.Add(ReadJson<Conversation>(zip, CloEntries.Chat(id)) ?? throw Missing(CloEntries.Chat(id)));
        foreach (var id in manifest.Projects)
            archive.Projects.Add(ReadJson<Project>(zip, CloEntries.Project(id)) ?? throw Missing(CloEntries.Project(id)));
        foreach (var r in manifest.Reconstructions)
        {
            var entry = zip.GetEntry(CloEntries.Reconstruction(r.Id)) ?? throw Missing(CloEntries.Reconstruction(r.Id));
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            archive.Reconstructions.Add(new Reconstruction
            {
                Id = r.Id, ConversationId = r.ConversationId, MessageId = r.MessageId, TargetName = r.TargetName,
                CreatedAt = r.CreatedAt, Method = r.Method, Text = reader.ReadToEnd(),
            });
        }
        foreach (var machine in manifest.Machines)
            if (zip.GetEntry(CloEntries.ClaudeCodeSnapshot(machine)) is not null)
                archive.ClaudeCode.Add(ReadJson<ClaudeCodeSnapshot>(zip, CloEntries.ClaudeCodeSnapshot(machine))!);
        return archive;
    }

    /// <summary>Entries whose content does not match the hash in the manifest (empty if the package is intact).</summary>
    public static IReadOnlyList<string> Verify(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = ReadJson<CloManifest>(zip, CloEntries.Manifest)!;
        var bad = new List<string>();
        foreach (var (name, expected) in manifest.Hashes)
        {
            var entry = zip.GetEntry(name);
            if (entry is null) { bad.Add(name); continue; }
            using var s = entry.Open();
            if (!Convert.ToHexStringLower(SHA256.HashData(s)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                bad.Add(name);
        }
        return bad;
    }

    private static T? ReadJson<T>(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw Missing(name);
        using var s = entry.Open();
        return JsonSerializer.Deserialize<T>(s, CloJson.Options);
    }

    private static InvalidDataException Missing(string name) => new($"The package has no {name}.");
}

/// <summary>Binary content read from a .clo package on disk.</summary>
public sealed class CloContent(string path, bool hasOriginalExport) : IArchiveContent
{
    public string Path { get; } = path;

    public Stream? OpenOriginalExport() => hasOriginalExport ? Open(CloEntries.OriginalExport) : null;

    public Stream? OpenBlob(Blob blob) => Open(blob.EntryName);

    public Stream? OpenClaudeCodeFile(ClaudeCodeSnapshot snapshot, ClaudeCodeFile file) =>
        Open(CloEntries.ClaudeCodeFile(snapshot.Machine, file.RelativePath));

    private Stream? Open(string entryName)
    {
        var zip = ZipFile.OpenRead(Path);
        if (zip.GetEntry(entryName) is not { } entry)
        {
            zip.Dispose();
            return null;
        }
        return new OwningStream(entry.Open(), zip);
    }
}

/// <summary>A stream that also disposes the object that owns it (the open ZIP).</summary>
internal sealed class OwningStream(Stream inner, IDisposable owner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner.Dispose();
        }
        base.Dispose(disposing);
    }
}
