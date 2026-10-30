using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Migrador.Core.Model;

namespace Migrador.Package;

/// <summary>Writes an <see cref="Archive"/> as a .clo package.</summary>
public static class CloWriter
{
    /// <summary>
    /// Writes the package to a temporary file next to <paramref name="path"/> and only then replaces it, so a failure
    /// never leaves a half-written package (and the archive may still be reading from the old one).
    /// </summary>
    public static void Write(string path, Archive archive, string? appVersion = null, IProgress<string>? progress = null)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                Write(stream, archive, appVersion, progress);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    public static void Write(Stream output, Archive archive, string? appVersion = null, IProgress<string>? progress = null)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        var content = archive.Content;

        var hasOriginal = false;
        using (var original = content?.OpenOriginalExport())
            if (original is not null)
            {
                progress?.Report(CloEntries.OriginalExport);
                // Already compressed: storing it again costs time and gains nothing.
                hashes[CloEntries.OriginalExport] = AddStream(zip, CloEntries.OriginalExport, original, CompressionLevel.NoCompression);
                hasOriginal = true;
            }

        foreach (var conversation in archive.Conversations)
            hashes[CloEntries.Chat(conversation.Id)] = AddJson(zip, CloEntries.Chat(conversation.Id), conversation);

        foreach (var project in archive.Projects)
            hashes[CloEntries.Project(project.Id)] = AddJson(zip, CloEntries.Project(project.Id), project);

        foreach (var blob in archive.Blobs)
        {
            using var source = content?.OpenBlob(blob)
                ?? throw new InvalidOperationException($"Blob {blob.Sha256} has no content.");
            progress?.Report(blob.EntryName);
            var actual = AddStream(zip, blob.EntryName, source, CompressionLevel.Optimal);
            if (!actual.Equals(blob.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Blob {blob.Sha256} does not match its content ({actual}).");
            hashes[blob.EntryName] = actual;
        }

        foreach (var r in archive.Reconstructions)
            hashes[CloEntries.Reconstruction(r.Id)] = AddBytes(zip, CloEntries.Reconstruction(r.Id), Encoding.UTF8.GetBytes(r.Text));

        foreach (var snapshot in archive.ClaudeCode)
        {
            foreach (var file in snapshot.Files)
            {
                var entry = CloEntries.ClaudeCodeFile(snapshot.Machine, file.RelativePath);
                using var source = content?.OpenClaudeCodeFile(snapshot, file);
                if (source is null)
                    throw new InvalidOperationException($"{file.RelativePath} ({snapshot.Machine}) has no content.");
                progress?.Report(entry);
                // A file may change between the inventory and the copy (an open session's transcript): the snapshot
                // describes what was actually copied.
                var counting = new CountingStream(source);
                file.Sha256 = hashes[entry] = AddStream(zip, entry, counting, CompressionLevel.Optimal);
                file.Size = counting.BytesRead;
            }
            hashes[CloEntries.ClaudeCodeSnapshot(snapshot.Machine)] = AddJson(zip, CloEntries.ClaudeCodeSnapshot(snapshot.Machine), snapshot);
        }

        var manifest = new CloManifest
        {
            CreatedAt = DateTimeOffset.Now,
            AppVersion = appVersion,
            Account = archive.Account,
            Source = archive.Report,
            HasOriginalExport = hasOriginal,
            Machines = archive.ClaudeCode.Select(s => s.Machine)
                .Concat(archive.Blobs.SelectMany(b => b.Locations).Select(l => l.Machine).OfType<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Conversations = archive.Conversations.Select(c => c.Id).ToList(),
            Projects = archive.Projects.Select(p => p.Id).ToList(),
            Blobs = archive.Blobs,
            Reconstructions = archive.Reconstructions.Select(r => new Reconstruction
            {
                Id = r.Id, ConversationId = r.ConversationId, MessageId = r.MessageId, TargetName = r.TargetName,
                CreatedAt = r.CreatedAt, Method = r.Method, Text = "",
            }).ToList(),
            Hashes = hashes,
        };
        AddBytes(zip, CloEntries.Manifest, JsonSerializer.SerializeToUtf8Bytes(manifest, CloJson.Indented));
    }

    private static string AddJson<T>(ZipArchive zip, string name, T value) =>
        AddBytes(zip, name, JsonSerializer.SerializeToUtf8Bytes(value, CloJson.Options));

    private static string AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        entry.Write(bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string AddStream(ZipArchive zip, string name, Stream source, CompressionLevel level)
    {
        using var entry = zip.CreateEntry(name, level).Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int n;
        while ((n = source.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, n);
            entry.Write(buffer, 0, n);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int n)
        {
            BytesRead += n;
            return n;
        }
    }
}
