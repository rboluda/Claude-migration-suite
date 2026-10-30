namespace Migrador.Core;

/// <summary>
/// How faithful a preserved item is to the original. Ordered from best to worst: aggregation always reports the worst
/// level plus a count per level, never an average.
/// </summary>
public enum Fidelity
{
    /// <summary>Identical, verified by hash.</summary>
    Original,
    /// <summary>Found by content and may differ (show a diff), or restored and confirmed by the user.</summary>
    Equivalent,
    /// <summary>Text only: no formatting, formulas or embedded images.</summary>
    ExtractedText,
    /// <summary>A description generated afterwards. Always shown as a reconstruction.</summary>
    Reconstruction,
    /// <summary>Only the reference remains.</summary>
    Lost,
}

/// <summary>States that are not fidelity levels but change what can be done with an item.</summary>
public enum ItemState
{
    None,
    /// <summary>Found by the app and not yet confirmed by the user. Never used in exports.</summary>
    PendingConfirmation,
    /// <summary>Several variants with the same identity: the user has to choose. Export is blocked.</summary>
    Collision,
    /// <summary>The user reviewed the loss and accepted it.</summary>
    AcceptedLoss,
}

/// <summary>Worst level plus a count per level.</summary>
public sealed record FidelitySummary(Fidelity Worst, IReadOnlyDictionary<Fidelity, int> Counts)
{
    public static FidelitySummary Empty { get; } = new(Fidelity.Original, new Dictionary<Fidelity, int>());

    public int Total => Counts.Values.Sum();

    public int this[Fidelity level] => Counts.TryGetValue(level, out var n) ? n : 0;

    public static FidelitySummary Of(IEnumerable<Fidelity> levels)
    {
        var counts = new Dictionary<Fidelity, int>();
        var worst = Fidelity.Original;
        foreach (var level in levels)
        {
            counts[level] = counts.GetValueOrDefault(level) + 1;
            if (level > worst)
                worst = level;
        }
        return counts.Count == 0 ? Empty : new FidelitySummary(worst, counts);
    }
}

public static class FidelityRules
{
    /// <summary>Extensions whose extracted text is the whole file (apart from encoding and line endings).</summary>
    private static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".html", ".htm", ".css",
        ".js", ".ts", ".tsx", ".jsx", ".py", ".cs", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".rb", ".php",
        ".sh", ".ps1", ".bat", ".sql", ".ini", ".toml", ".log", ".svg", ".tex",
    };

    /// <summary>
    /// Level of an item for which the export only keeps the extracted text. For plain-text files that text is the
    /// file, but it cannot be verified by hash, so it is <see cref="Fidelity.Equivalent"/> at best.
    /// </summary>
    public static Fidelity ForExtractedText(string? fileName) =>
        fileName is not null && PlainText.Contains(Path.GetExtension(fileName)) ? Fidelity.Equivalent : Fidelity.ExtractedText;
}
