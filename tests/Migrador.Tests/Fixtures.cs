using System.IO.Compression;

namespace Migrador.Tests;

/// <summary>Builds export ZIPs from the folders in tests/fixtures.</summary>
internal static class Fixtures
{
    public static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>ZIP with every file of the fixture folder except its README, in a fresh temporary folder.</summary>
    public static string ExportZip(string name, TempDir temp)
    {
        var zipPath = Path.Combine(temp.Path, name + ".zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.GetFiles(Folder(name)).Where(f => !Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase)).Order())
            zip.CreateEntryFromFile(file, Path.GetFileName(file));
        return zipPath;
    }

    /// <summary>The repository root (the folder of Migrador.slnx).</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Migrador.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Migrador.slnx not found above the test folder.");
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "migrador-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* the OS cleans temp eventually */ }
    }
}
