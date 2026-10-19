using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Codiff.ViewModels;

public partial class DiffLineViewModel : ObservableObject
{
    // Colors as on GitHub: the old version of changed lines is red, the new one green, and the
    // changed words inside a line are darker. Rows without a line on one side are gray there.
    private static readonly DiffColors LightColors = new(
        Normal: Brush("#FFFFFF"), Filler: Brush("#F3F4F6"),
        DeletedLine: Brush("#FFEBE9"), DeletedNumber: Brush("#FFD7D5"), DeletedWord: Brush("#FFC1C0"),
        AddedLine: Brush("#E6FFEC"), AddedNumber: Brush("#CCFFD8"), AddedWord: Brush("#ABF2BC"));

    private static readonly DiffColors DarkColors = new(
        Normal: Brush("#1E1E1E"), Filler: Brush("#262628"),
        DeletedLine: Brush("#3F2624"), DeletedNumber: Brush("#552B29"), DeletedWord: Brush("#75322F"),
        AddedLine: Brush("#203224"), AddedNumber: Brush("#223F27"), AddedWord: Brush("#24522D"));

    // Reused for theme notifications, which are raised for every line at once.
    private static readonly PropertyChangedEventArgs[] ThemeChangedArgs =
    [
        new(nameof(LeftBackground)), new(nameof(RightBackground)),
        new(nameof(LeftNumberBackground)), new(nameof(RightNumberBackground)),
        new(nameof(LeftWordHighlightBrush)), new(nameof(RightWordHighlightBrush))
    ];

    private static DiffColors Colors => ThemeService.Instance.IsDark ? DarkColors : LightColors;

    public DiffLine Model { get; }

    public string LeftLineNumberText => Model.LeftLineNumber.HasValue ? Model.LeftLineNumber.Value.ToString() : string.Empty;
    public string RightLineNumberText => Model.RightLineNumber.HasValue ? Model.RightLineNumber.Value.ToString() : string.Empty;

    /// <summary>The row has a line of the left file (it is not an empty row facing an added line).</summary>
    public bool HasLeftLine => Model.LeftLineNumber.HasValue;

    /// <summary>The row has a line of the right file (it is not an empty row facing a deleted line).</summary>
    public bool HasRightLine => Model.RightLineNumber.HasValue;

    [ObservableProperty]
    private string _leftText;

    [ObservableProperty]
    private string _rightText;

    /// <summary>Syntax highlighting of the left line (null until it has been tokenized, or without highlighting).</summary>
    [ObservableProperty]
    private LineSyntax? _leftSyntax;

    /// <summary>Syntax highlighting of the right line.</summary>
    [ObservableProperty]
    private LineSyntax? _rightSyntax;

    public LineType LineType => Model.LineType;

    /// <summary>True for the lines of the difference that navigation and Copy to Right/Left act on.</summary>
    [ObservableProperty]
    private bool _isInCurrentHunk;

    /// <summary>Start of the highlighted search match in the left text (-1 = none).</summary>
    [ObservableProperty]
    private int _leftMatchStart = -1;

    /// <summary>Start of the highlighted search match in the right text (-1 = none).</summary>
    [ObservableProperty]
    private int _rightMatchStart = -1;

    /// <summary>Length of the highlighted search match.</summary>
    [ObservableProperty]
    private int _matchLength;

    public event Action<DiffLineViewModel>? LeftTextEdited;
    public event Action<DiffLineViewModel>? RightTextEdited;

    partial void OnLeftTextChanged(string value) => LeftTextEdited?.Invoke(this);
    partial void OnRightTextChanged(string value) => RightTextEdited?.Invoke(this);

    public IBrush LeftBackground => Model.LineType switch
    {
        LineType.Unchanged => Colors.Normal,
        LineType.Modified or LineType.Deleted => Colors.DeletedLine,
        _ => Colors.Filler
    };

    public IBrush RightBackground => Model.LineType switch
    {
        LineType.Unchanged => Colors.Normal,
        LineType.Modified or LineType.Added => Colors.AddedLine,
        _ => Colors.Filler
    };

    /// <summary>Background of the left line number; null for unchanged lines (the normal line number background).</summary>
    public IBrush? LeftNumberBackground => Model.LineType switch
    {
        LineType.Unchanged => null,
        LineType.Modified or LineType.Deleted => Colors.DeletedNumber,
        _ => Colors.Filler
    };

    /// <summary>Background of the right line number; null for unchanged lines.</summary>
    public IBrush? RightNumberBackground => Model.LineType switch
    {
        LineType.Unchanged => null,
        LineType.Modified or LineType.Added => Colors.AddedNumber,
        _ => Colors.Filler
    };

    /// <summary>Background of the changed words in the left text of a modified line.</summary>
    public IBrush? LeftWordHighlightBrush => Model.LineType == LineType.Modified ? Colors.DeletedWord : null;

    /// <summary>Background of the changed words in the right text of a modified line.</summary>
    public IBrush? RightWordHighlightBrush => Model.LineType == LineType.Modified ? Colors.AddedWord : null;

    public DiffLineViewModel(DiffLine model)
    {
        Model = model;
        _leftText = model.LeftText;
        _rightText = model.RightText;
    }

    /// <summary>Notifies the view that all brush-bound properties have changed (called on theme toggle).</summary>
    public void NotifyThemeChanged()
    {
        if (Model.LineType == LineType.Unchanged)
        {
            OnPropertyChanged(ThemeChangedArgs[0]);
            OnPropertyChanged(ThemeChangedArgs[1]);
            return;
        }
        foreach (var args in ThemeChangedArgs)
            OnPropertyChanged(args);
    }

    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    private sealed record DiffColors(
        IBrush Normal, IBrush Filler,
        IBrush DeletedLine, IBrush DeletedNumber, IBrush DeletedWord,
        IBrush AddedLine, IBrush AddedNumber, IBrush AddedWord);
}
