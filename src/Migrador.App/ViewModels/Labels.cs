using Migrador.Core;
using Migrador.Core.Model;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App.ViewModels;

/// <summary>Interface names of model values.</summary>
public static class Labels
{
    public static string Of(Fidelity f) => f switch
    {
        Fidelity.Original => T("Original"),
        Fidelity.Equivalent => T("Equivalente"),
        Fidelity.ExtractedText => T("Texto extraído"),
        Fidelity.Reconstruction => T("Reconstrucción"),
        _ => T("Perdido"),
    };

    public static string Of(MessageRole r) => r switch
    {
        MessageRole.Human => T("Tú"),
        MessageRole.Assistant => T("Asistente"),
        _ => T("Desconocido"),
    };

    public static string Of(ClaudeCodeFileKind k) => k switch
    {
        ClaudeCodeFileKind.Instructions => T("Instrucciones (CLAUDE.md)"),
        ClaudeCodeFileKind.Settings => T("Ajustes"),
        ClaudeCodeFileKind.Skill => T("Skills"),
        ClaudeCodeFileKind.Agent => T("Agentes"),
        ClaudeCodeFileKind.Command => T("Comandos"),
        ClaudeCodeFileKind.Plugin => T("Plugins"),
        ClaudeCodeFileKind.Transcript => T("Transcripciones de sesiones"),
        ClaudeCodeFileKind.Memory => T("Memoria"),
        _ => T("Otros archivos"),
    };

    /// <summary>"Original" or "Perdido (3) · Texto extraído (2) · Original (10)": worst first, never an average.</summary>
    public static string Of(FidelitySummary s) =>
        s.Total == 0
            ? T("Original")
            : string.Join(" · ", s.Counts.OrderByDescending(c => c.Key).Select(c => $"{Of(c.Key)} ({c.Value})"));

    public static string Date(DateTimeOffset? d) => d is { } v ? v.ToLocalTime().ToString("g", Culture) : T("sin fecha");

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes} B" : string.Format(Culture, "{0:0.#} {1}", v, units[i]);
    }
}
