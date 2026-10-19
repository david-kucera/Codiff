using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Codiff.Models;

namespace Codiff.Controls;

/// <summary>Text colors of the syntax categories: GitHub's palettes for the light and the dark theme.</summary>
public sealed class SyntaxPalette
{
    // Order of SyntaxCategory: Text, Comment, Keyword, Constant, Function, Variable, Tag, String,
    // Invalid, Deleted, Inserted.
    public static readonly SyntaxPalette Light = new(
        "#1F2328", "#6E7781", "#CF222E", "#0550AE", "#8250DF", "#953800", "#116329", "#0A3069", "#82071E", "#82071E", "#116329");

    public static readonly SyntaxPalette Dark = new(
        "#D4D4D4", "#8B949E", "#FF7B72", "#79C0FF", "#D2A8FF", "#FFA657", "#7EE787", "#A5D6FF", "#FFA198", "#FFA198", "#7EE787");

    private readonly IImmutableSolidColorBrush[] _brushes;

    // Text run properties per category, for the typeface and size they were last created for
    // (all lines use the same font, so this is created once).
    private readonly TextRunProperties?[] _runProperties;
    private Typeface _typeface;
    private double _fontSize;

    private SyntaxPalette(params string[] colors)
    {
        _brushes = colors.Select(c => (IImmutableSolidColorBrush)new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();
        _runProperties = new TextRunProperties?[colors.Length];
    }

    public static SyntaxPalette For(ThemeVariant? theme) => theme == ThemeVariant.Dark ? Dark : Light;

    /// <summary>The normal text color.</summary>
    public IBrush Text => _brushes[(int)SyntaxCategory.Text];

    public IBrush this[SyntaxCategory category] => _brushes[(int)category];

    /// <summary>Formatting of a run of the given category (cached; for the UI thread only).</summary>
    public TextRunProperties RunProperties(SyntaxCategory category, Typeface typeface, double fontSize)
    {
        if (!typeface.Equals(_typeface) || fontSize != _fontSize)
        {
            Array.Clear(_runProperties);
            _typeface = typeface;
            _fontSize = fontSize;
        }
        return _runProperties[(int)category] ??=
            new GenericTextRunProperties(typeface, fontRenderingEmSize: fontSize, foregroundBrush: _brushes[(int)category]);
    }
}
