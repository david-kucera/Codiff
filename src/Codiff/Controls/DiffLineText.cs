using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Codiff.Models;

namespace Codiff.Controls;

/// <summary>
/// Draws the text of one side of a diff line: colored by syntax, over the highlights of the changed
/// words, the search match and the selection. It sits behind the line's TextBox, which has an
/// invisible text and only provides the caret and editing; both lay out the text with the same
/// font, so the caret and the drawn characters line up.
/// </summary>
public class DiffLineText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<DiffLineText, string?>(nameof(Text));

    /// <summary>Syntax highlighting of the line (ignored once it no longer matches <see cref="Text"/>).</summary>
    public static readonly StyledProperty<LineSyntax?> SyntaxProperty =
        AvaloniaProperty.Register<DiffLineText, LineSyntax?>(nameof(Syntax));

    /// <summary>Word-level pieces of the line as it was compared (see <see cref="DiffLine.LeftSpans"/>).</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffSpan>?> SpansProperty =
        AvaloniaProperty.Register<DiffLineText, IReadOnlyList<DiffSpan>?>(nameof(Spans));

    public static readonly StyledProperty<IBrush?> SpanBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(SpanBrush));

    /// <summary>Start of the search match in <see cref="Text"/> (-1 = none).</summary>
    public static readonly StyledProperty<int> MatchStartProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(MatchStart), -1);

    public static readonly StyledProperty<int> MatchLengthProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(MatchLength));

    public static readonly StyledProperty<IBrush?> MatchBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(MatchBrush));

    /// <summary>Selection of the TextBox above (start and end may be in either order).</summary>
    public static readonly StyledProperty<int> SelectionStartProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(SelectionStart));

    public static readonly StyledProperty<int> SelectionEndProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(SelectionEnd));

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(SelectionBrush));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<DiffLineText>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<DiffLineText>();

    /// <summary>Horizontal position of the first character (the TextBox's left padding).</summary>
    public static readonly StyledProperty<double> TextOffsetProperty =
        AvaloniaProperty.Register<DiffLineText, double>(nameof(TextOffset));

    private TextLayout? _layout;

    public DiffLineText()
    {
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    static DiffLineText()
    {
        AffectsRender<DiffLineText>(TextProperty, SyntaxProperty, SpansProperty, SpanBrushProperty, MatchStartProperty,
            MatchLengthProperty, MatchBrushProperty, SelectionStartProperty, SelectionEndProperty, SelectionBrushProperty,
            FontFamilyProperty, FontSizeProperty, TextOffsetProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public LineSyntax? Syntax
    {
        get => GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    public IReadOnlyList<DiffSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    public IBrush? SpanBrush
    {
        get => GetValue(SpanBrushProperty);
        set => SetValue(SpanBrushProperty, value);
    }

    public int MatchStart
    {
        get => GetValue(MatchStartProperty);
        set => SetValue(MatchStartProperty, value);
    }

    public int MatchLength
    {
        get => GetValue(MatchLengthProperty);
        set => SetValue(MatchLengthProperty, value);
    }

    public IBrush? MatchBrush
    {
        get => GetValue(MatchBrushProperty);
        set => SetValue(MatchBrushProperty, value);
    }

    public int SelectionStart
    {
        get => GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public int SelectionEnd
    {
        get => GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double TextOffset
    {
        get => GetValue(TextOffsetProperty);
        set => SetValue(TextOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == SyntaxProperty ||
            change.Property == FontFamilyProperty || change.Property == FontSizeProperty)
        {
            ResetLayout();
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        // The text colors depend on the theme.
        ResetLayout();
        InvalidateVisual();
    }

    private void ResetLayout()
    {
        _layout?.Dispose();
        _layout = null;
    }

    public override void Render(DrawingContext context)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
            return;

        _layout ??= CreateLayout(text);
        double height = Bounds.Height;

        if (SpanBrush is { } spanBrush && Spans is { Count: > 0 } spans && SpansMatchText(spans, text))
        {
            int position = 0;
            foreach (var span in spans)
            {
                if (span.IsChanged && span.Text.Length > 0)
                    Fill(context, spanBrush, position, span.Text.Length, height);
                position += span.Text.Length;
            }
        }

        if (MatchBrush is { } matchBrush && MatchStart >= 0 && MatchLength > 0 && MatchStart + MatchLength <= text.Length)
            Fill(context, matchBrush, MatchStart, MatchLength, height);

        int selectionStart = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, text.Length);
        int selectionEnd = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, text.Length);
        if (SelectionBrush is { } selectionBrush && selectionEnd > selectionStart)
            Fill(context, selectionBrush, selectionStart, selectionEnd - selectionStart, height);

        // At the top, like the TextBox's presenter places its text.
        _layout.Draw(context, new Point(TextOffset, 0));
    }

    private TextLayout CreateLayout(string text)
    {
        var palette = SyntaxPalette.For(ActualThemeVariant);
        var typeface = new Typeface(FontFamily);

        List<ValueSpan<TextRunProperties>>? runs = null;
        if (Syntax is { Runs.Count: > 0 } syntax && syntax.Text == text)
        {
            runs = new List<ValueSpan<TextRunProperties>>(syntax.Runs.Count);
            foreach (var run in syntax.Runs)
                runs.Add(new ValueSpan<TextRunProperties>(run.Start, run.Length, palette.RunProperties(run.Category, typeface, FontSize)));
        }

        return new TextLayout(text, typeface, FontSize, palette.Text, textStyleOverrides: runs);
    }

    private void Fill(DrawingContext context, IBrush brush, int start, int length, double height)
    {
        foreach (var rect in _layout!.HitTestTextRange(start, length))
            context.FillRectangle(brush, new Rect(TextOffset + rect.X, 0, Math.Max(rect.Width, 1), height));
    }

    /// <summary>
    /// The spans describe the text as it was compared; once the line has been edited they no
    /// longer fit and nothing is highlighted.
    /// </summary>
    private static bool SpansMatchText(IReadOnlyList<DiffSpan> spans, string text)
    {
        int position = 0;
        foreach (var span in spans)
        {
            if (position + span.Text.Length > text.Length ||
                !text.AsSpan(position, span.Text.Length).SequenceEqual(span.Text))
                return false;
            position += span.Text.Length;
        }
        return position == text.Length;
    }
}
