using System.Windows.Markup;
using Migrador.Core.Localization;

namespace Migrador.App.Controls;

/// <summary>
/// A fixed interface text in the chosen language: <c>Text="{l:T 'Guardar'}"</c>. The Spanish text is the key of the
/// translation (see <see cref="Loc"/>).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
