using System.IO;
using System.Text.Json;

namespace Migrador.App.Services;

public sealed record AppSettings
{
    /// <summary>Interface language; null = the system's.</summary>
    public string? Language { get; init; }
    public bool CheckUpdates { get; init; } = true;
    /// <summary>Download and install new versions without asking.</summary>
    public bool InstallUpdates { get; init; } = true;
    public DateTimeOffset? LastUpdateCheck { get; init; }
}

/// <summary>Settings in %LOCALAPPDATA%\Migrador\settings.json.</summary>
public sealed class SettingsService
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Migrador");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public SettingsService()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings: start with the defaults.
        }
    }

    public AppSettings Current { get; private set; } = new();

    public void Save(AppSettings settings)
    {
        Current = settings;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save the settings is not worth interrupting the user.
        }
    }
}
