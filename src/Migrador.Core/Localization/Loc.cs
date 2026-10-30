using System.Globalization;
using System.Text.Json;

namespace Migrador.Core.Localization;

/// <summary>
/// The interface language. Texts are written in Spanish in the code and the XAML, and that same text is the key of
/// the translation in Languages/&lt;language&gt;.json (embedded resource). A missing translation shows the Spanish
/// text: never an empty key. The language is chosen at startup; changing it restarts the app.
/// </summary>
public static class Loc
{
    public const string Spanish = "es";
    public const string English = "en";

    /// <summary>Available languages, each with its name in its own language.</summary>
    public static IReadOnlyList<(string Code, string Name)> Available { get; } =
    [
        (Spanish, "Español"),
        (English, "English"),
    ];

    private static IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    public static string Current { get; private set; } = Spanish;

    /// <summary>Culture for numbers and dates.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>The system language if it is Spanish; English otherwise.</summary>
    public static string Default() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Spanish ? Spanish : English;

    public static void Activate(string? language)
    {
        Current = Available.Any(a => a.Code == language) ? language! : Default();
        Culture = CultureInfo.GetCultureInfo(Current == English ? "en-US" : "es-ES");
        _table = Current == Spanish ? new Dictionary<string, string>() : Table(Current);
    }

    /// <summary>Translations of <paramref name="language"/>: Spanish text → translated text.</summary>
    public static IReadOnlyDictionary<string, string> Table(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Migrador.Core.Languages.{language}.json");
        return stream is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }

    /// <summary>The text in the interface language.</summary>
    public static string T(string text) => _table.TryGetValue(text, out var t) ? t : text;

    /// <summary>Like <see cref="string.Format(IFormatProvider, string, object[])"/>, with the format translated.</summary>
    public static string T(string format, params object?[] args) => string.Format(Culture, T(format), args);
}
