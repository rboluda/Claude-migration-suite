using System.Text.Json;

namespace Migrador.Core.Updates;

/// <summary>A version published on GitHub Releases.</summary>
/// <param name="ExeName">Name of the executable asset, or null if the release has none.</param>
/// <param name="SumsUrl">URL of SHA256SUMS.txt, or null if the release has none.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? ExeName, string? ExeUrl, string? SumsUrl, string Notes)
{
    /// <summary>Everything needed to install it without asking.</summary>
    public bool CanInstall => ExeName is not null && ExeUrl is not null && SumsUrl is not null;
}

/// <summary>Reading the GitHub API answer for the latest release. No network here: see the app's update service.</summary>
public static class Releases
{
    public const string Repository = "rboluda/Claude-migration-suite";
    public const string ProjectUrl = $"https://github.com/{Repository}";
    public const string AuthorName = "rboluda";
    public const string AuthorUrl = "https://github.com/rboluda";
    public const string LatestUrl = $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>Executables are published as "Migrador-x.y.z.exe".</summary>
    public const string ExePrefix = "Migrador-";

    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (ParseVersion(tag) is not { } version)
            return null;

        string? exeName = null, exeUrl = null, sumsUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    sumsUrl = url;
                else if (name.StartsWith(ExePrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    (exeName, exeUrl) = (name, url);
            }

        return new ReleaseInfo(version, tag,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? ProjectUrl : ProjectUrl,
            exeName, exeUrl, sumsUrl,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    /// <summary>"v1.2.0", "1.2" or "1.2.0-beta" → 1.2.0. Null if it is not a version.</summary>
    public static Version? ParseVersion(string text)
    {
        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    /// <summary>The hash of <paramref name="fileName"/> in a SHA256SUMS.txt ("hash  name" lines), or null.</summary>
    public static string? ExpectedHash(string sums, string fileName) =>
        sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].Trim().TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .FirstOrDefault(h => h.Length == 64);

    /// <summary>1.2 and 1.2.0.0 compare as 1.2.0.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
