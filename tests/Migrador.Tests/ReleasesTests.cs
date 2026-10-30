using Migrador.Core.Updates;

namespace Migrador.Tests;

public class ReleasesTests
{
    private const string Latest = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/rboluda/Claude-migration-suite/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "body": "Cambios",
          "assets": [
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.com/SHA256SUMS.txt" },
            { "name": "Migrador-1.2.0.exe", "browser_download_url": "https://example.com/Migrador-1.2.0.exe" }
          ]
        }
        """;

    [Fact]
    public void Reads_the_latest_release()
    {
        var r = Releases.Parse(Latest)!;

        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("Migrador-1.2.0.exe", r.ExeName);
        Assert.Equal("https://example.com/SHA256SUMS.txt", r.SumsUrl);
        Assert.True(r.CanInstall);
        Assert.True(Releases.IsNewer(r, new Version(1, 1, 9, 0)));
        Assert.False(Releases.IsNewer(r, new Version(1, 2, 0, 0)));
    }

    [Fact]
    public void Ignores_drafts_and_prereleases()
    {
        Assert.Null(Releases.Parse(Latest.Replace("\"draft\": false", "\"draft\": true")));
        Assert.Null(Releases.Parse(Latest.Replace("\"prerelease\": false", "\"prerelease\": true")));
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    public void Parses_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), Releases.ParseVersion(tag));

    [Fact]
    public void Finds_the_hash_of_a_file_in_sha256sums()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  LEEME.txt\n{hash} *Migrador-1.2.0.exe\n";

        Assert.Equal(hash, Releases.ExpectedHash(sums, "Migrador-1.2.0.exe"));
        Assert.Null(Releases.ExpectedHash(sums, "otro.exe"));
    }
}
