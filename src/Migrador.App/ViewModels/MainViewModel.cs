using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Migrador.App.Services;
using Migrador.App.Views;
using Migrador.ClaudeCode;
using Migrador.Core;
using Migrador.Core.Model;
using Migrador.Core.Updates;
using Migrador.Package;
using Migrador.Parsers.Export;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App.ViewModels;

public enum Section
{
    Chats,
    Projects,
    ClaudeCode,
    Machines,
    Losses,
    RestoreImages,
    Basket,
}

public sealed partial class NavItem(Section section, string label) : ObservableObject
{
    public Section Section { get; } = section;
    public string Label { get; } = label;
    [ObservableProperty] public partial int Count { get; set; }
}

/// <summary>A row of the middle list.</summary>
public sealed record ListItem(string Title, string Subtitle, string? Badge, object Model);

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly SettingsService _settings;
    private readonly UpdateService _updates;
    private Archive? _archive;
    private string? _path;
    private ReleaseInfo? _release;

    public MainViewModel(SettingsService settings, UpdateService updates)
    {
        _settings = settings;
        _updates = updates;
        Sections =
        [
            new(Section.Chats, T("Chats")),
            new(Section.Projects, T("Proyectos")),
            new(Section.ClaudeCode, T("Claude Code")),
            new(Section.Machines, T("Equipos")),
            new(Section.Losses, T("Pérdidas")),
            new(Section.RestoreImages, T("Restaurar imágenes")),
            new(Section.Basket, T("Cesta")),
        ];
        SelectedSection = Sections[0];
        Reader = Welcome();
    }

    public IReadOnlyList<NavItem> Sections { get; }

    [ObservableProperty] public partial NavItem? SelectedSection { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<ListItem> Items { get; set; } = [];
    [ObservableProperty] public partial ListItem? SelectedItem { get; set; }
    [ObservableProperty] public partial object? Reader { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportExportCommand), nameof(OpenCloCommand), nameof(SaveCloCommand), nameof(AddClaudeCodeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = T("Listo.");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial bool HasUnsavedChanges { get; set; }

    public string WindowTitle => _path is null && _archive is null
        ? "Migrador"
        : $"{(HasUnsavedChanges ? "• " : "")}{(_path is null ? T("Sin guardar") : Path.GetFileName(_path))} — Migrador";

    // --- Updates ---
    [ObservableProperty] public partial string? UpdateMessage { get; set; }
    [ObservableProperty] public partial bool CanRestartToUpdate { get; set; }
    [ObservableProperty] public partial bool CanDownloadUpdate { get; set; }

    partial void OnSelectedSectionChanged(NavItem? value) => RefreshList();

    partial void OnSearchTextChanged(string value) => RefreshList();

    partial void OnSelectedItemChanged(ListItem? value)
    {
        if (value is null || _archive is null)
            return;
        Reader = value.Model switch
        {
            Conversation c => new ConversationReader(c, _archive),
            Project p => new ProjectReader(p),
            ClaudeCodeSnapshot s => new ClaudeCodeReader(s),
            LossItem l => new LossReader(l),
            Placeholder ph => ph,
            _ => null,
        };
    }

    /// <summary>After the window is shown: a .clo passed on the command line, and the daily update check.</summary>
    public async Task StartAsync(string[] args)
    {
        if (args.FirstOrDefault(a => a.EndsWith(".clo", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) is { } clo)
            await OpenAsync(clo);
        if (UpdateService.PendingVersion() is { } pending)
            ShowPending(pending);
        else
            await CheckUpdatesQuietlyAsync();
    }

    // --- Archive ---

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportExportAsync()
    {
        if (!ConfirmDiscard())
            return;
        var dialog = new OpenFileDialog
        {
            Title = T("Importar el export de la cuenta"),
            Filter = T("Export de datos (*.zip)") + "|*.zip",
        };
        if (dialog.ShowDialog() != true)
            return;
        await RunAsync(T("Leyendo el export…"), progress => ExportParser.ParseAsync(dialog.FileName, progress), archive =>
        {
            Load(archive, null, unsaved: true);
            var r = archive.Report!;
            Status = T("Importados {0} chats y {1} proyectos. Guárdalo como .clo para archivarlo.", archive.Conversations.Count, archive.Projects.Count)
                     + (r.UnknownEntries.Count + r.Warnings.Count > 0 ? " " + T("Avisos del lector: {0}.", r.UnknownEntries.Count + r.Warnings.Count) : "");
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenCloAsync()
    {
        if (!ConfirmDiscard())
            return;
        var dialog = new OpenFileDialog { Title = T("Abrir un archivo .clo"), Filter = T("Archivo de Migrador (*.clo)") + "|*.clo" };
        if (dialog.ShowDialog() == true)
            await OpenAsync(dialog.FileName);
    }

    private Task OpenAsync(string path) => RunAsync(T("Abriendo {0}…", Path.GetFileName(path)), _ => Task.FromResult(CloReader.Read(path)), archive =>
    {
        Load(archive, path, unsaved: false);
        Status = T("Abierto {0}: {1} chats, {2} proyectos.", Path.GetFileName(path), archive.Conversations.Count, archive.Projects.Count);
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveCloAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = T("Guardar como .clo"),
            Filter = T("Archivo de Migrador (*.clo)") + "|*.clo",
            FileName = _path is null ? $"cuenta-{DateTime.Now:yyyy-MM-dd}.clo" : Path.GetFileName(_path),
        };
        if (dialog.ShowDialog() != true)
            return;
        var archive = _archive!;
        var path = dialog.FileName;
        await RunAsync(T("Guardando…"), progress =>
        {
            CloWriter.Write(path, archive, UpdateService.CurrentVersion.ToString(3), progress);
            var bad = CloReader.Verify(path);
            if (bad.Count > 0)
                throw new InvalidDataException(T("El archivo guardado no supera la comprobación: {0}", string.Join(", ", bad.Take(5))));
            // From now on the content is read from the saved package.
            return Task.FromResult(CloReader.Read(path));
        }, reread =>
        {
            Load(reread, path, unsaved: false);
            Status = T("Guardado y comprobado: {0}.", path);
        });
    }

    private bool CanSave() => IsIdle() && _archive is not null;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AddClaudeCodeAsync()
    {
        var root = ClaudeCodeInventory.DefaultRoot;
        if (!Directory.Exists(root))
        {
            MessageBox.Show(Owner(), T("No hay carpeta de Claude Code en este equipo ({0}).", root), "Migrador", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RunAsync(T("Inventariando {0}…", root), progress => Task.FromResult(ClaudeCodeInventory.Take(root, Environment.MachineName, progress)), snapshot =>
        {
            var archive = _archive ?? new Archive();
            archive.ClaudeCode.RemoveAll(s => s.Machine.Equals(snapshot.Machine, StringComparison.OrdinalIgnoreCase));
            archive.ClaudeCode.Add(snapshot);
            var local = new LocalClaudeCodeContent(snapshot);
            archive.Content = archive.Content is null ? local : new CompositeContent(archive.Content, local);
            Load(archive, _path, unsaved: true);
            SelectedSection = Sections.First(s => s.Section == Section.ClaudeCode);
            Status = T("Claude Code de {0}: {1} archivos, {2} proyectos; {3} excluidos (credenciales y cachés). Guarda el .clo para conservarlo.",
                snapshot.Machine, snapshot.Files.Count, snapshot.Projects.Count, snapshot.Excluded.Count);
        });
    }

    private void Load(Archive archive, string? path, bool unsaved)
    {
        _archive = archive;
        _path = path;
        HasUnsavedChanges = unsaved;
        OnPropertyChanged(nameof(WindowTitle));
        SaveCloCommand.NotifyCanExecuteChanged();
        Count(Section.Chats, archive.Conversations.Count);
        Count(Section.Projects, archive.Projects.Count);
        Count(Section.ClaudeCode, archive.ClaudeCode.Count);
        Count(Section.Machines, Machines().Count);
        Count(Section.Losses, Losses().Count);
        var f = archive.Fidelity();
        Summary = T("Fidelidad de todo el archivo: {0}", Labels.Of(f));
        RefreshList();
    }

    private void Count(Section section, int n) => Sections.First(s => s.Section == section).Count = n;

    // --- List ---

    private void RefreshList()
    {
        SelectedItem = null;
        if (_archive is null)
        {
            Items = [];
            Reader = Welcome();
            return;
        }
        var q = SearchText.Trim();
        bool Match(params string?[] texts) => q.Length == 0 || texts.Any(t => t is not null && Compare.IndexOf(t, q, Loose) >= 0);

        Items = SelectedSection?.Section switch
        {
            Section.Chats => _archive.Conversations
                .Where(c => Match(c.Title) || c.Messages.Any(m => Match(m.Text)))
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => new ListItem(
                    string.IsNullOrWhiteSpace(c.Title) ? T("(sin título)") : c.Title,
                    $"{Labels.Date(c.UpdatedAt ?? c.CreatedAt)} · {T("{0} mensajes", c.Messages.Count)}",
                    c.Fidelity() is { Worst: not Fidelity.Original } f ? Labels.Of(f.Worst) : null,
                    c))
                .ToList(),
            Section.Projects => _archive.Projects
                .Where(p => Match(p.Name, p.Description, p.Instructions) || p.Documents.Any(d => Match(d.FileName, d.Content)))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new ListItem(p.Name, T("{0} documentos", p.Documents.Count),
                    p.Fidelity() is { Worst: not Fidelity.Original } f ? Labels.Of(f.Worst) : null, p))
                .ToList(),
            Section.ClaudeCode or Section.Machines => Machines()
                .Where(m => Match(m))
                .Select(m => _archive.ClaudeCode.FirstOrDefault(s => s.Machine == m) is { } s
                    ? new ListItem(m, T("{0} archivos · {1} proyectos", s.Files.Count, s.Projects.Count), null, s)
                    : new ListItem(m, T("Sin copia de Claude Code"), null, new Placeholder(m, T("De este equipo solo hay archivos encontrados, no una copia de Claude Code."))))
                .ToList(),
            Section.Losses => Losses()
                .Where(l => Match(l.Name, l.Owner))
                .Select(l => new ListItem(l.Name, l.Owner, Labels.Of(l.Item.Fidelity), l))
                .ToList(),
            _ => [],
        };

        if (SelectedSection?.Section is Section.RestoreImages or Section.Basket)
            Reader = SelectedSection.Section == Section.RestoreImages
                ? new Placeholder(T("Restaurar imágenes"), T("Buscar en tus equipos las imágenes que el export solo referencia, comparándolas por fecha, metadatos y contenido, y pedirte confirmación antes de usarlas. Llega en una próxima versión."))
                : new Placeholder(T("Cesta"), T("Juntar chats y proyectos para exportarlos de una vez, con vista previa de lo que se generará y de lo que se pierde. Llega en una próxima versión."));
        else
            Reader = Items.Count == 0
                ? new Placeholder(T("Nada que mostrar"), q.Length > 0 ? T("Ningún elemento coincide con «{0}».", q) : T("Esta sección está vacía."))
                : null;
    }

    private List<string> Machines() => _archive is null
        ? []
        : _archive.ClaudeCode.Select(s => s.Machine)
            .Concat(_archive.Blobs.SelectMany(b => b.Locations).Select(l => l.Machine).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private List<LossItem> Losses()
    {
        if (_archive is null)
            return [];
        var result = new List<LossItem>();
        foreach (var c in _archive.Conversations)
        {
            var title = string.IsNullOrWhiteSpace(c.Title) ? T("(sin título)") : c.Title;
            var ordered = c.Messages.OrderBy(m => m.CreatedAt).ToList();
            foreach (var m in c.Messages)
                foreach (var (item, name) in m.Attachments.Select(a => ((ResolvableItem)a, a.FileName)).Concat(m.Files.Select(f => ((ResolvableItem)f, f.FileName))))
                    if (IsLoss(item))
                    {
                        var label = name ?? T("(sin nombre)");
                        var stem = Path.GetFileNameWithoutExtension(label);
                        var later = stem.Length < 3
                            ? []
                            : ordered.Where(o => o.CreatedAt > m.CreatedAt && o.Text.Contains(stem, StringComparison.OrdinalIgnoreCase)).ToList();
                        result.Add(new LossItem(label, title, item, m, later));
                    }
        }
        foreach (var p in _archive.Projects)
            foreach (var d in p.Documents.Where(IsLoss))
                result.Add(new LossItem(d.FileName ?? T("(sin nombre)"), p.Name, d, null, []));
        return result;
    }

    private static bool IsLoss(ResolvableItem i) =>
        i.State != ItemState.AcceptedLoss && i.Fidelity is Fidelity.ExtractedText or Fidelity.Reconstruction or Fidelity.Lost;

    private static Placeholder Welcome() => new(
        T("Te damos la bienvenida a Migrador"),
        T("Migrador guarda todo el contenido de tu cuenta en un único archivo .clo, sin resumir nada, y te ayuda a llevarlo a otra cuenta. Todo se procesa en tu equipo.")
        + "\n\n" + T("Para empezar, pide el export de tus datos: en claude.ai, Ajustes → Privacidad → Exportar datos. Te llegará un correo con un enlace a un .zip (caduca en unas horas). Después, aquí: Archivo → Importar export.")
        + "\n\n" + T("También puedes añadir la carpeta de Claude Code de este equipo (Archivo → Añadir Claude Code de este equipo) o abrir un .clo que ya tengas."));

    // --- Updates ---

    private async Task CheckUpdatesQuietlyAsync()
    {
        try
        {
            if (await _updates.CheckAsync(force: false) is { } r)
                await OfferAsync(r, quiet: true);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        try
        {
            Status = T("Buscando actualizaciones…");
            if (await _updates.CheckAsync(force: true) is { } r)
                await OfferAsync(r, quiet: false);
            else
            {
                Status = T("Tienes la última versión ({0}).", UpdateService.CurrentVersion.ToString(3));
                MessageBox.Show(Owner(), Status, "Migrador", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception e)
        {
            Status = T("No se pudo comprobar si hay actualizaciones: {0}", e.Message);
        }
    }

    private async Task OfferAsync(ReleaseInfo r, bool quiet)
    {
        _release = r;
        if (UpdateService.CanSelfUpdate && r.CanInstall && (_settings.Current.InstallUpdates || !quiet))
        {
            await DownloadUpdateAsync();
            return;
        }
        UpdateMessage = T("Hay una versión nueva: {0}.", r.Version.ToString(3));
        CanDownloadUpdate = UpdateService.CanSelfUpdate && r.CanInstall;
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_release is not { } r)
            return;
        CanDownloadUpdate = false;
        try
        {
            UpdateMessage = T("Descargando la versión {0}…", r.Version.ToString(3));
            await _updates.DownloadAsync(r, new Progress<double>(p => UpdateMessage = T("Descargando la versión {0}… {1:P0}", r.Version.ToString(3), p)));
            ShowPending(r.Version.ToString(3));
        }
        catch (Exception e)
        {
            UpdateMessage = T("No se pudo descargar la versión {0}: {1}", r.Version.ToString(3), e.Message);
            CanDownloadUpdate = true;
        }
    }

    private void ShowPending(string version)
    {
        UpdateMessage = T("La versión {0} está lista: se instalará al cerrar Migrador.", version);
        CanRestartToUpdate = true;
    }

    [RelayCommand]
    private void RestartToUpdate()
    {
        if (IsBusy)
        {
            MessageBox.Show(Owner(), T("Espera a que termine la operación en curso."), "Migrador", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmDiscard())
            return;
        HasUnsavedChanges = false;
        if (UpdateService.ApplyAndRestart())
            Application.Current.Shutdown();
        else
            UpdateMessage = T("No se pudo instalar la actualización. Se volverá a intentar más tarde.");
    }

    // --- Windows ---

    [RelayCommand]
    private void OpenSettings() => new SettingsWindow { Owner = Owner(), DataContext = new SettingsViewModel(_settings, this) }.ShowDialog();

    [RelayCommand]
    private void ShowAbout() => new AboutWindow { Owner = Owner(), DataContext = new AboutViewModel() }.ShowDialog();

    [RelayCommand]
    private static void OpenProjectPage() => AboutViewModel.Open(Releases.ProjectUrl);

    [RelayCommand]
    private static void Exit() => Application.Current.MainWindow?.Close();

    // --- Helpers ---

    private bool IsIdle() => !IsBusy;

    /// <summary>True if nothing would be lost (no unsaved changes, or the user agrees).</summary>
    public bool ConfirmDiscard()
    {
        if (!HasUnsavedChanges)
            return true;
        var answer = MessageBox.Show(Owner(),
            T("Hay datos importados sin guardar en un .clo. ¿Quieres guardarlos antes?"),
            "Migrador", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            SaveCloCommand.Execute(null);
            return false;
        }
        return answer == MessageBoxResult.No;
    }

    /// <summary>Closing while an operation is running loses it: ask first.</summary>
    public bool ConfirmClose() =>
        (!IsBusy || MessageBox.Show(Owner(), T("Hay una operación en curso. ¿Cerrar de todas formas?"), "Migrador",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        && ConfirmDiscard();

    /// <summary>Runs <paramref name="work"/> in the background and then <paramref name="done"/> on the interface thread.</summary>
    private async Task RunAsync<TResult>(string message, Func<IProgress<string>, Task<TResult>> work, Action<TResult> done)
    {
        IsBusy = true;
        Status = message;
        var progress = new Progress<string>(p => Status = $"{message} {p}");
        try
        {
            done(await Task.Run(() => work(progress)));
        }
        catch (Exception e)
        {
            Status = T("Error: {0}", e.Message);
            MessageBox.Show(Owner(), e.Message, T("Algo ha fallado"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Window Owner() => Application.Current.MainWindow!;
}
