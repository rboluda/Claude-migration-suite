using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Migrador.Core.Updates;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App.Services;

/// <summary>
/// Unattended updates from GitHub Releases: checks at most once a day, downloads the new executable in the
/// background, verifies its SHA-256 against the release's SHA256SUMS.txt and its product name and version, and
/// replaces the running executable when the app closes (or right away, if the user asks to restart).
/// Nothing about the user is sent: only what any HTTPS request carries (IP address and a User-Agent with the version).
/// </summary>
public sealed class UpdateService(SettingsService settings)
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(20);

    // Before Http: static fields are initialized in order and CreateHttp needs the version.
    public static Version CurrentVersion { get; } = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateHttp();

    private static string UpdateDir => Path.Combine(SettingsService.Folder, "update");
    private static string PendingPath => Path.Combine(UpdateDir, "pending.json");

    private sealed record PendingInfo(string Version, string File, string Sha256);

    public static string? ExePath => Environment.ProcessPath;

    /// <summary>
    /// Only the published single-file executable updates itself (a build run from Visual Studio has Migrador.dll next
    /// to it), and only if its folder is writable.
    /// </summary>
    public static bool CanSelfUpdate =>
        ExePath is { } exe
        && Path.GetFileName(exe).Equals("Migrador.exe", StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, "Migrador.dll"))
        && IsWritable(Path.GetDirectoryName(exe)!);

    /// <summary>The newer published version, or null (no newer one, not time to check yet, or no connection).</summary>
    public async Task<ReleaseInfo?> CheckAsync(bool force, CancellationToken ct = default)
    {
        var s = settings.Current;
        if (!force && (!s.CheckUpdates || s.LastUpdateCheck is { } last && DateTimeOffset.Now - last < Interval))
            return null;
        try
        {
            var json = await Http.GetStringAsync(Releases.LatestUrl, ct);
            settings.Save(settings.Current with { LastUpdateCheck = DateTimeOffset.Now });
            return Releases.Parse(json) is { } r && Releases.IsNewer(r, CurrentVersion) ? r : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // No connection or nothing published yet: not an error for the user.
            if (force)
                throw;
            return null;
        }
    }

    /// <summary>
    /// Downloads and verifies the new executable, and leaves it pending for <see cref="ApplyPending"/>. Throws if
    /// anything does not match, without leaving anything half-done.
    /// </summary>
    public async Task DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!release.CanInstall)
            throw new InvalidOperationException(T("La versión publicada no incluye el ejecutable o su suma de comprobación."));

        var expected = Releases.ExpectedHash(await Http.GetStringAsync(release.SumsUrl, ct), release.ExeName!)
                       ?? throw new InvalidOperationException(T("SHA256SUMS.txt no contiene el ejecutable."));
        Directory.CreateDirectory(UpdateDir);
        var target = Path.Combine(UpdateDir, release.ExeName!);
        var part = target + ".part";
        try
        {
            using (var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long done = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0)
                        progress?.Report((double)done / total);
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("El archivo descargado no coincide con su suma de comprobación (SHA-256). No se ha instalado nada."));
            }
            CheckIdentity(part, release.Version);
            File.Move(part, target, overwrite: true);
            await File.WriteAllTextAsync(PendingPath, JsonSerializer.Serialize(new PendingInfo(release.Version.ToString(3), target, expected)), ct);
        }
        catch
        {
            try { File.Delete(part); } catch { /* best effort */ }
            throw;
        }
    }

    private static void CheckIdentity(string file, Version version)
    {
        var info = FileVersionInfo.GetVersionInfo(file);
        if (info.ProductName != "Migrador" || !(info.ProductVersion ?? "").StartsWith(version.ToString(3), StringComparison.Ordinal))
            throw new InvalidOperationException(T("El archivo descargado no es Migrador {0}. No se ha instalado nada.", version.ToString(3)));
    }

    /// <summary>Version downloaded, verified and newer than the running one, or null.</summary>
    public static string? PendingVersion()
    {
        try
        {
            if (!File.Exists(PendingPath))
                return null;
            var p = JsonSerializer.Deserialize<PendingInfo>(File.ReadAllText(PendingPath));
            return p is not null && File.Exists(p.File) && Releases.ParseVersion(p.Version) is { } v && v > Normalize(CurrentVersion)
                ? p.Version
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the running executable with the pending one (checking its SHA-256 again). Windows lets a running
    /// executable be renamed, not deleted: it becomes Migrador.exe.old and is deleted on the next start.
    /// </summary>
    public static bool ApplyPending()
    {
        if (!CanSelfUpdate || PendingVersion() is null)
            return false;
        try
        {
            var p = JsonSerializer.Deserialize<PendingInfo>(File.ReadAllText(PendingPath))!;
            using (var fs = File.OpenRead(p.File))
                if (!Convert.ToHexString(SHA256.HashData(fs)).Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Discard();
                    return false;
                }
            var exe = ExePath!;
            var old = exe + ".old";
            if (File.Exists(old))
                File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(p.File, exe);
            }
            catch
            {
                File.Move(old, exe); // put the running version back
                throw;
            }
            Discard();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Applies the pending update and starts the new version.</summary>
    public static bool ApplyAndRestart()
    {
        if (!ApplyPending())
            return false;
        Process.Start(new ProcessStartInfo(ExePath!) { UseShellExecute = false });
        return true;
    }

    /// <summary>On startup: removes the previous executable and downloads that are no longer needed.</summary>
    public static void Cleanup()
    {
        try
        {
            if (ExePath is { } exe && File.Exists(exe + ".old"))
                File.Delete(exe + ".old");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still locked: next time.
        }
        if (PendingVersion() is null)
            Discard();
    }

    private static void Discard()
    {
        try
        {
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Next time.
        }
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".migrador-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Migrador/{CurrentVersion.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
