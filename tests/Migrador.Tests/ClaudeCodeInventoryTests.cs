using Migrador.ClaudeCode;
using Migrador.Core.Model;

namespace Migrador.Tests;

public class ClaudeCodeInventoryTests
{
    // Real folder names seen in ~/.claude/projects (2026-10-30).
    [Theory]
    [InlineData(@"C:\PROYECTOS\Migrador", "C--PROYECTOS-Migrador")]
    [InlineData(@"C:\HERCULES\hercules2\AzorDroid", "C--HERCULES-hercules2-AzorDroid")]
    [InlineData(@"C:\Users\u\OneDrive - Empresa S.A.U\L2 - La cazadora", "C--Users-u-OneDrive---Empresa-S-A-U-L2---La-cazadora")]
    public void Encodes_paths_like_claude_code(string path, string folder) =>
        Assert.Equal(folder, ClaudeCodeInventory.EncodeProjectPath(path));

    [Theory]
    [InlineData(".credentials.json", true)]
    [InlineData(".claude.json", true)]
    [InlineData(".claude.json.backup", true)]
    [InlineData("policy-limits.json.signature.json", true)]
    [InlineData("ide/41234.lock", true)]
    [InlineData("backups/.claude.json.backup.123", true)]
    [InlineData("plugins/cache/x/plugin.json", true)]
    [InlineData("CLAUDE.md", false)]
    [InlineData("settings.json", false)]
    [InlineData("plugins/installed_plugins.json", false)]
    [InlineData("projects/C--x/abc.jsonl", false)]
    public void Leaves_out_credentials_and_caches(string rel, bool excluded) =>
        Assert.Equal(excluded, ClaudeCodeInventory.IsExcluded(rel));

    [Theory]
    [InlineData("CLAUDE.md", ClaudeCodeFileKind.Instructions)]
    [InlineData("settings.local.json", ClaudeCodeFileKind.Settings)]
    [InlineData("skills/x/SKILL.md", ClaudeCodeFileKind.Skill)]
    [InlineData("agents/revisor.md", ClaudeCodeFileKind.Agent)]
    [InlineData("projects/C--x/abc.jsonl", ClaudeCodeFileKind.Transcript)]
    [InlineData("projects/C--x/memory/MEMORY.md", ClaudeCodeFileKind.Memory)]
    [InlineData("projects/C--x/abc/subagents/a.jsonl", ClaudeCodeFileKind.Transcript)]
    public void Classifies_files(string rel, ClaudeCodeFileKind kind) =>
        Assert.Equal(kind, ClaudeCodeInventory.KindOf(rel));

    [Fact]
    public void Resolves_the_path_even_if_the_session_started_in_another_folder()
    {
        using var temp = new TempDir();
        var transcript = Path.Combine(temp.Path, "s.jsonl");
        File.WriteAllLines(transcript,
        [
            """{"type":"queue-operation"}""",
            """{"type":"user","cwd":"C:\\Temp\\scratch-1"}""",
            """{"type":"user","cwd":"C:\\PROYECTOS\\AppRouter"}""",
        ]);

        Assert.Equal(@"C:\PROYECTOS\AppRouter", ClaudeCodeInventory.ResolvePath("C--PROYECTOS-AppRouter", [transcript]));
        Assert.Null(ClaudeCodeInventory.ResolvePath("C--Otro", [transcript]));
    }

    [Fact]
    public void Reads_the_origin_remote()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".git"));
        File.WriteAllText(Path.Combine(temp.Path, ".git", "config"), """
            [core]
            	bare = false
            [remote "upstream"]
            	url = https://example.com/otro.git
            [remote "origin"]
            	url = https://github.com/usuario/repo.git
            	fetch = +refs/heads/*:refs/remotes/origin/*
            """);

        Assert.Equal("https://github.com/usuario/repo.git", ClaudeCodeInventory.GitRemote(temp.Path));
    }
}
