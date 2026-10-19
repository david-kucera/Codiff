using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Codiff.Models;

namespace Codiff.Controls;

/// <summary>
/// Paints backgrounds behind parts of a line's text: the changed words of a modified line and
/// the current search match. It sits behind the line's TextBox and lays the text out with the
/// same font, so the highlights line up with the glyphs drawn by the TextBox.
/// </summary>
public class LineHighlighter : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LineHighlighter, string?>(nameof(Text));

    /// <summary>Word-level pieces of the line as it was compared (see <see cref="DiffLine.LeftSpans"/>).</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffSpan>?> SpansProperty =
        AvaloniaProperty.Register<LineHighlighter, IReadOnlyList<DiffSpan>?>(nameof(Spans));

    public static readonly StyledProperty<IBrush?> SpanBrushProperty =
        AvaloniaProperty.Register<LineHighlighter, IBrush?>(nameof(SpanBrush));

    /// <summary>Start of the search match in <see cref="Text"/> (-1 = none).</summary>
    public static readonly StyledProperty<int> MatchStartProperty =
        AvaloniaProperty.Register<LineHighlighter, int>(nameof(MatchStart), -1);

    public static readonly StyledProperty<int> MatchLengthProperty =
        AvaloniaProperty.Register<LineHighlighter, int>(nameof(MatchLength));

    public static readonly StyledProperty<IBrush?> MatchBrushProperty =
        AvaloniaProperty.Register<LineHighlighter, IBrush?>(nameof(MatchBrush));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<LineHighlighter>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<LineHighlighter>();

    /// <summary>Horizontal position of the first character (the TextBox's left padding).</summary>
    public static readonly StyledProperty<double> TextOffsetProperty =
        AvaloniaProperty.Register<LineHighlighter, double>(nameof(TextOffset));

    private TextLayout? _layout;

    static LineHighlighter()
    {
        AffectsRender<LineHighlighter>(TextProperty, SpansProperty, SpanBrushProperty, MatchStartProperty,
            MatchLengthProperty, MatchBrushProperty, FontFamilyProperty, FontSizeProperty, TextOffsetProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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
        if (change.Property == TextProperty || change.Property == FontFamilyProperty || change.Property == FontSizeProperty)
        {
            _layout?.Dispose();
            _layout = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
            return;

        bool hasSpans = SpanBrush != null && Spans is { Count: > 0 } && SpansMatchText(Spans, text);
        bool hasMatch = MatchBrush != null && MatchStart >= 0 && MatchLength > 0 && MatchStart + MatchLength <= text.Length;
        if (!hasSpans && !hasMatch)
            return;

        _layout ??= new TextLayout(text, new Typeface(FontFamily), FontSize, Brushes.Transparent);
        double height = Bounds.Height;

        if (hasSpans)
        {
            int position = 0;
            foreach (var span in Spans!)
            {
                if (span.IsChanged && span.Text.Length > 0)
                    Fill(context, SpanBrush!, position, span.Text.Length, height);
                position += span.Text.Length;
            }
        }

        if (hasMatch)
            Fill(context, MatchBrush!, MatchStart, MatchLength, height);
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
