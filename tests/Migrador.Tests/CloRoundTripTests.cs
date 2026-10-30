using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Migrador.ClaudeCode;
using Migrador.Core.Model;
using Migrador.Package;
using Migrador.Parsers.Export;

namespace Migrador.Tests;

public class CloRoundTripTests
{
    [Fact]
    public async Task Export_to_clo_and_back_gives_the_same_model()
    {
        using var temp = new TempDir();
        var export = Fixtures.ExportZip("export-synthetic", temp);
        var parsed = await ExportParser.ParseAsync(export);
        var clo = Path.Combine(temp.Path, "cuenta.clo");

        CloWriter.Write(clo, parsed, "0.1.0");
        var read = CloReader.Read(clo);

        Assert.Equal(Json(parsed.Conversations), Json(read.Conversations));
        Assert.Equal(Json(parsed.Projects), Json(read.Projects));
        Assert.Equal(Json(parsed.Account), Json(read.Account));
        Assert.Equal(Json(parsed.Report), Json(read.Report));
        Assert.Empty(CloReader.Verify(clo));
    }

    [Fact]
    public async Task The_original_export_is_stored_untouched()
    {
        using var temp = new TempDir();
        var export = Fixtures.ExportZip("export-synthetic", temp);
        var clo = Path.Combine(temp.Path, "cuenta.clo");

        CloWriter.Write(clo, await ExportParser.ParseAsync(export));
        using var stored = CloReader.Read(clo).Content!.OpenOriginalExport()!;

        Assert.Equal(SHA256.HashData(File.ReadAllBytes(export)), SHA256.HashData(stored));
    }

    [Fact]
    public async Task A_clo_can_be_saved_again_over_itself()
    {
        using var temp = new TempDir();
        var clo = Path.Combine(temp.Path, "cuenta.clo");
        CloWriter.Write(clo, await ExportParser.ParseAsync(Fixtures.ExportZip("export-synthetic", temp)));

        var first = CloReader.Read(clo);
        CloWriter.Write(clo, first);
        var second = CloReader.Read(clo);

        Assert.Equal(Json(first.Conversations), Json(second.Conversations));
        Assert.NotNull(second.Content!.OpenOriginalExport());
        Assert.Empty(CloReader.Verify(clo));
    }

    [Fact]
    public async Task Claude_code_snapshot_is_packaged_without_credentials()
    {
        using var temp = new TempDir();
        var root = Path.Combine(temp.Path, ".claude");
        Write(root, "CLAUDE.md", "# Preferencias");
        Write(root, "settings.json", """{ "cleanupPeriodDays": 90 }""");
        Write(root, ".credentials.json", "SECRETO");
        Write(root, "ide/12345.lock", "token");
        Write(root, "skills/demo/SKILL.md", "skill");
        Write(root, "projects/C--demo/sesion.jsonl", """{"type":"user","cwd":"C:\\demo"}""");
        Write(root, "projects/C--demo/memory/MEMORY.md", "- nota");

        var snapshot = ClaudeCodeInventory.Take(root, "EQUIPO-1");
        var archive = await ExportParser.ParseAsync(Fixtures.ExportZip("export-synthetic", temp));
        archive.ClaudeCode.Add(snapshot);
        archive.Content = new CompositeContent(archive.Content!, new LocalClaudeCodeContent(snapshot));
        var clo = Path.Combine(temp.Path, "cuenta.clo");

        CloWriter.Write(clo, archive);
        var read = CloReader.Read(clo);

        Assert.Equal(90, read.ClaudeCode.Single().CleanupPeriodDays);
        Assert.Equal(["CLAUDE.md", "projects/C--demo/memory/MEMORY.md", "projects/C--demo/sesion.jsonl", "settings.json", "skills/demo/SKILL.md"],
            read.ClaudeCode.Single().Files.Select(f => f.RelativePath).Order(StringComparer.Ordinal));
        Assert.Equal("C:\\demo", read.ClaudeCode.Single().Projects.Single().Path);
        using var zip = ZipFile.OpenRead(clo);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("credentials") || e.FullName.Contains("/ide/"));
        Assert.Empty(CloReader.Verify(clo));
    }

    private static void Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, CloJson.Options);
}
