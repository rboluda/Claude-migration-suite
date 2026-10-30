using System.Text.RegularExpressions;
using Migrador.Core.Localization;

namespace Migrador.Tests;

/// <summary>Every interface text has its English version, and the English table has no leftovers.</summary>
public partial class TranslationTests
{
    [GeneratedRegex("""(?<![\w.])T\(\s*"((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex CodeText();

    [GeneratedRegex("""\{l:T '([^']*)'\}""")]
    private static partial Regex XamlText();

    /// <summary>Every text passed to T() in the code or {l:T '…'} in the XAML.</summary>
    internal static SortedSet<string> SourceTexts()
    {
        var src = Path.Combine(Fixtures.RepoRoot(), "src");
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput))
            foreach (Match m in CodeText().Matches(File.ReadAllText(file)))
                texts.Add(Regex.Unescape(m.Groups[1].Value));
        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories).Where(NotBuildOutput))
            foreach (Match m in XamlText().Matches(File.ReadAllText(file)))
                texts.Add(m.Groups[1].Value);
        return texts;
    }

    private static bool NotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    [Fact]
    public void Every_text_has_an_english_translation()
    {
        var english = Loc.Table(Loc.English);
        var missing = SourceTexts().Where(t => !english.ContainsKey(t) || string.IsNullOrWhiteSpace(english[t])).ToList();

        Assert.True(missing.Count == 0, "Missing in Languages/en.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_english_table_has_no_unused_texts()
    {
        var used = SourceTexts();
        var unused = Loc.Table(Loc.English).Keys.Where(k => !used.Contains(k)).ToList();

        Assert.True(unused.Count == 0, "Unused in Languages/en.json:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void Format_placeholders_match()
    {
        var english = Loc.Table(Loc.English);
        foreach (var (es, en) in english)
            Assert.True(Placeholders(es).SetEquals(Placeholders(en)), $"Placeholders differ: «{es}» → «{en}»");
    }

    private static HashSet<string> Placeholders(string s) => Regex.Matches(s, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).ToHashSet();
}
