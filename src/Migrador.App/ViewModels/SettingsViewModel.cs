using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Migrador.App.Services;
using Migrador.Core.Localization;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _main;

    public SettingsViewModel(SettingsService settings, MainViewModel main)
    {
        _settings = settings;
        _main = main;
        Language = Loc.Current;
        CheckUpdates = settings.Current.CheckUpdates;
        InstallUpdates = settings.Current.InstallUpdates;
    }

    public IReadOnlyList<LanguageOption> Languages { get; } = Loc.Available.Select(a => new LanguageOption(a.Code, a.Name)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRestart), nameof(RestartNotice))]
    public partial string Language { get; set; }

    [ObservableProperty] public partial bool CheckUpdates { get; set; }
    [ObservableProperty] public partial bool InstallUpdates { get; set; }

    /// <summary>The language chosen is not the one in use: it applies after restarting.</summary>
    public bool NeedsRestart => Language != Loc.Current;

    // In the chosen language, not the current one: whoever chooses it is who understands it.
    public string RestartNotice => Language == Loc.English
        ? "The new language is applied when Migrador restarts."
        : "El idioma nuevo se aplica al reiniciar Migrador.";

    public bool CannotSelfUpdate { get; } = !UpdateService.CanSelfUpdate;

    public string Version { get; } = T("Versión instalada: {0}", UpdateService.CurrentVersion.ToString(3));

    [RelayCommand]
    private void Save(Window window)
    {
        _settings.Save(_settings.Current with { Language = Language, CheckUpdates = CheckUpdates, InstallUpdates = InstallUpdates });
        window.DialogResult = true;
        if (NeedsRestart && UpdateService.ExePath is { } exe && _main.ConfirmClose())
        {
            _main.HasUnsavedChanges = false;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            Application.Current.Shutdown();
        }
    }
}

public sealed partial class AboutViewModel : ObservableObject
{
    public string Version { get; } = T("Versión {0}", UpdateService.CurrentVersion.ToString(3));
    public string Author { get; } = Core.Updates.Releases.AuthorName;
    public string Copyright { get; } =
        System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttributes(typeof(System.Reflection.AssemblyCopyrightAttribute), false)
            .OfType<System.Reflection.AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright ?? "";

    [RelayCommand]
    private static void OpenAuthor() => Open(Core.Updates.Releases.AuthorUrl);

    [RelayCommand]
    private static void OpenRepository() => Open(Core.Updates.Releases.ProjectUrl);

    [RelayCommand]
    private static void ReportIssue() => Open(Core.Updates.Releases.ProjectUrl + "/issues");

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
