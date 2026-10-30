using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Migrador.ClaudeCode;
using Migrador.Core;
using Migrador.Core.Model;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App.ViewModels;

/// <summary>A conversation in the reader, showing one branch at a time.</summary>
public sealed partial class ConversationReader : ObservableObject
{
    private readonly ConversationTree _tree;

    public ConversationReader(Conversation conversation, Archive archive)
    {
        _tree = new ConversationTree(conversation);
        Title = string.IsNullOrWhiteSpace(conversation.Title) ? T("(sin título)") : conversation.Title;
        var project = conversation.ProjectId is { } pid ? archive.Projects.FirstOrDefault(p => p.Id == pid)?.Name : null;
        Info = string.Join(" · ", new[]
        {
            T("Creado {0}", Labels.Date(conversation.CreatedAt)),
            T("actualizado {0}", Labels.Date(conversation.UpdatedAt)),
            T("{0} mensajes", conversation.Messages.Count),
            project is null ? null : T("proyecto «{0}»", project),
            _tree.HasBranches ? T("puntos con ramas: {0}", _tree.BranchPointCount) : null,
        }.OfType<string>());
        FidelityText = T("Fidelidad: {0}", Labels.Of(conversation.Fidelity()));
        Warning = conversation.TreeSource == TreeSource.InferredLinear && conversation.Messages.Count > 1
            ? T("El export no indica qué mensaje responde a cuál: se muestran en orden y, si había ramas, no están en los datos.")
            : null;
        Show(_tree.ActivePath());
    }

    public string Title { get; }
    public string Info { get; }
    public string FidelityText { get; }
    public string? Warning { get; }
    public ObservableCollection<MessageView> Messages { get; } = [];

    private void Show(IReadOnlyList<Message> path)
    {
        Messages.Clear();
        foreach (var m in path)
        {
            var siblings = _tree.Siblings(m);
            Messages.Add(new MessageView(m, siblings.Count > 1 ? siblings.ToList().IndexOf(m) : -1, siblings.Count, this));
        }
    }

    internal void SwitchTo(Message message) => Show(_tree.PathThrough(message));

    internal IReadOnlyList<Message> SiblingsOf(Message message) => _tree.Siblings(message);
}

public sealed partial class MessageView(Message message, int siblingIndex, int siblingCount, ConversationReader reader) : ObservableObject
{
    public string Role => Labels.Of(message.Role) + (message.Role == MessageRole.Unknown && message.RawRole is { } r ? $" ({r})" : "");
    public bool IsHuman => message.Role == MessageRole.Human;
    public string Date => Labels.Date(message.CreatedAt);
    public string Text => message.Text.Length > 0
        ? message.Text
        : string.Join("\n\n", message.Content.Select(c => c.Text).Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>Thinking and tool blocks, which the plain text does not include.</summary>
    public IReadOnlyList<string> OtherBlocks { get; } = message.Content
        .Where(c => c.Type is not ("text" or ""))
        .Select(c => $"[{c.Type}] {c.Text ?? c.Raw.ToString()}")
        .ToList();

    public IReadOnlyList<string> Items { get; } = message.Attachments
        .Select(a => $"📎 {a.FileName ?? T("(sin nombre)")} · {Labels.Of(a.Fidelity)}")
        .Concat(message.Files.Select(f => $"🖼 {f.FileName ?? T("(sin nombre)")} · {Labels.Of(f.Fidelity)}"))
        .ToList();

    public bool HasBranches => siblingCount > 1;
    public string BranchText => T("Versión {0} de {1}", siblingIndex + 1, siblingCount);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => reader.SwitchTo(reader.SiblingsOf(message)[siblingIndex - 1]);

    private bool CanGoPrevious() => siblingIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => reader.SwitchTo(reader.SiblingsOf(message)[siblingIndex + 1]);

    private bool CanGoNext() => siblingIndex >= 0 && siblingIndex < siblingCount - 1;
}

public sealed record DocumentView(string FileName, string Fidelity, string Content);

public sealed class ProjectReader(Project project)
{
    public string Title { get; } = string.IsNullOrWhiteSpace(project.Name) ? T("(sin nombre)") : project.Name;
    public string Info { get; } = string.Join(" · ", T("Creado {0}", Labels.Date(project.CreatedAt)), T("{0} documentos", project.Documents.Count));
    public string FidelityText { get; } = T("Fidelidad: {0}", Labels.Of(project.Fidelity()));
    public string? Description { get; } = project.Description;
    public string Instructions { get; } = project.Instructions ?? T("(sin instrucciones)");
    public IReadOnlyList<DocumentView> Documents { get; } = project.Documents
        .Select(d => new DocumentView(d.FileName ?? T("(sin nombre)"), Labels.Of(d.Fidelity), d.Content ?? ""))
        .ToList();
}

public sealed record CodeProjectView(string Title, string Details);

public sealed class ClaudeCodeReader(ClaudeCodeSnapshot s)
{
    public string Title { get; } = T("Claude Code en {0}", s.Machine);
    public string Info { get; } = T("Copia de {0} tomada el {1} · {2} archivos · {3}",
        s.SourceRoot, Labels.Date(s.TakenAt), s.Files.Count, Labels.Size(s.Files.Sum(f => f.Size)));

    public string CleanupWarning { get; } = T("Claude Code borra solo el historial de sesiones con más de {0} días ({1}). Lo que ya se haya borrado no se puede recuperar.",
        s.CleanupPeriodDays ?? ClaudeCodeInventory.DefaultCleanupPeriodDays,
        s.CleanupPeriodDays is null ? T("valor por defecto") : "cleanupPeriodDays");

    public IReadOnlyList<string> Kinds { get; } = s.Files.GroupBy(f => f.Kind).OrderBy(g => g.Key)
        .Select(g => $"{Labels.Of(g.Key)}: {g.Count()} · {Labels.Size(g.Sum(f => f.Size))}")
        .ToList();

    public IReadOnlyList<CodeProjectView> Projects { get; } = s.Projects.OrderByDescending(p => p.LastActivity)
        .Select(p => new CodeProjectView(
            p.Path ?? T("{0} (ruta desconocida)", p.FolderName),
            string.Join(" · ", new[]
            {
                T("{0} sesiones", p.SessionCount),
                p.HasMemory ? T("con memoria") : null,
                p.GitRemote,
                T("última actividad {0}", Labels.Date(p.LastActivity)),
            }.OfType<string>())))
        .ToList();

    public string ExcludedTitle { get; } = T("No se copian ({0}): credenciales, tokens de sesión y cachés", s.Excluded.Count);
    public IReadOnlyList<string> Excluded { get; } = s.Excluded;
}

/// <summary>Detail of a loss: what it was, where, what remains, what is lost, what is unknown and its impact.</summary>
public sealed class LossReader
{
    public LossReader(LossItem loss)
    {
        Title = loss.Name;
        Level = T("Nivel: {0}", Labels.Of(loss.Item.Fidelity));
        Where = loss.Message is { } m
            ? T("En «{0}», mensaje de {1} del {2}.", loss.Owner, Labels.Of(m.Role), Labels.Date(m.CreatedAt))
            : T("En el proyecto «{0}».", loss.Owner);
        (Remains, Lost) = loss.Item switch
        {
            { Fidelity: Fidelity.Lost } => (T("Solo la referencia: el nombre del archivo."), T("Todo el contenido.")),
            Attachment or ProjectDocument when loss.Item.Fidelity == Fidelity.ExtractedText =>
                (T("El texto que se extrajo del archivo."), T("El formato, las fórmulas, las imágenes internas y el archivo original.")),
            _ => (T("Una descripción generada a posteriori."), T("El original.")),
        };
        Unknown = T("Si el original sigue en algún equipo o en la nube: se buscará en la siguiente fase (local → nube → tú).");
        Impact = loss.LaterMentions.Count == 0
            ? T("Ningún mensaje posterior lo menciona por su nombre.")
            : T("{0} mensajes posteriores lo mencionan:", loss.LaterMentions.Count);
        Mentions = loss.LaterMentions.Select(m => $"{Labels.Of(m.Role)} · {Labels.Date(m.CreatedAt)}: {Excerpt(m.Text, loss.Name)}").ToList();
    }

    public string Title { get; }
    public string Level { get; }
    public string Where { get; }
    public string Remains { get; }
    public string Lost { get; }
    public string Unknown { get; }
    public string Impact { get; }
    public IReadOnlyList<string> Mentions { get; }

    private static string Excerpt(string text, string term)
    {
        var i = Math.Max(0, text.IndexOf(Path.GetFileNameWithoutExtension(term), StringComparison.OrdinalIgnoreCase));
        var start = Math.Max(0, i - 60);
        var s = text.Substring(start, Math.Min(160, text.Length - start)).ReplaceLineEndings(" ");
        return (start > 0 ? "…" : "") + s + (start + 160 < text.Length ? "…" : "");
    }
}

/// <summary>An item that is not original, with where it is.</summary>
public sealed record LossItem(string Name, string Owner, ResolvableItem Item, Message? Message, IReadOnlyList<Message> LaterMentions);

public sealed record Placeholder(string Title, string Text);
