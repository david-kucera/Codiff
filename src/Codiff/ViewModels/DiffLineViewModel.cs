using System.ComponentModel;
using Avalonia.Media;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Codiff.ViewModels;

public class SpanViewModel
{
    public string Text { get; set; } = string.Empty;
    public IBrush Background { get; set; } = Brushes.Transparent;
}

public partial class DiffLineViewModel : ObservableObject
{
    // Light-theme brushes
    private static readonly IBrush ModifiedBrushLight = new SolidColorBrush(Color.Parse("#FFFACD"));
    private static readonly IBrush OneSidedContentBrushLight = new SolidColorBrush(Color.Parse("#CCFFCC"));
    private static readonly IBrush FillerBrushLight = new SolidColorBrush(Color.Parse("#EEEEEE"));
    private static readonly IBrush NormalBrushLight = Brushes.White;

    // Dark-theme brushes
    private static readonly IBrush ModifiedBrushDark = new SolidColorBrush(Color.Parse("#3A3000"));
    private static readonly IBrush OneSidedContentBrushDark = new SolidColorBrush(Color.Parse("#003000"));
    private static readonly IBrush FillerBrushDark = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush NormalBrushDark = new SolidColorBrush(Color.Parse("#1E1E1E"));

    // Highlights of changed words inside a line (shared by all lines)
    private static readonly IBrush ChangedWordLeftModifiedBrush = new SolidColorBrush(Color.Parse("#FFB347"));
    private static readonly IBrush ChangedWordRightModifiedBrush = new SolidColorBrush(Color.Parse("#66CC66"));
    private static readonly IBrush ChangedWordAddedBrush = new SolidColorBrush(Color.Parse("#88EE88"));
    private static readonly IBrush ChangedWordDeletedBrush = new SolidColorBrush(Color.Parse("#FF9999"));
    private static readonly IBrush ChangedWordLeftModifiedBrushDark = new SolidColorBrush(Color.Parse("#8A5A00"));
    private static readonly IBrush ChangedWordRightModifiedBrushDark = new SolidColorBrush(Color.Parse("#2E6B2E"));

    // Reused for theme notifications, which are raised for every line at once.
    private static readonly PropertyChangedEventArgs LeftBackgroundChangedArgs = new(nameof(LeftBackground));
    private static readonly PropertyChangedEventArgs RightBackgroundChangedArgs = new(nameof(RightBackground));
    private static readonly PropertyChangedEventArgs LeftWordHighlightChangedArgs = new(nameof(LeftWordHighlightBrush));
    private static readonly PropertyChangedEventArgs RightWordHighlightChangedArgs = new(nameof(RightWordHighlightBrush));

    private static IBrush ModifiedBrush => ThemeService.Instance.IsDark ? ModifiedBrushDark : ModifiedBrushLight;
    private static IBrush OneSidedContentBrush => ThemeService.Instance.IsDark ? OneSidedContentBrushDark : OneSidedContentBrushLight;
    private static IBrush FillerBrush => ThemeService.Instance.IsDark ? FillerBrushDark : FillerBrushLight;
    private static IBrush NormalBrush => ThemeService.Instance.IsDark ? NormalBrushDark : NormalBrushLight;

    private IReadOnlyList<SpanViewModel>? _leftSpanViewModels;
    private IReadOnlyList<SpanViewModel>? _rightSpanViewModels;

    public DiffLine Model { get; }

    public string LeftLineNumberText => Model.LeftLineNumber.HasValue ? Model.LeftLineNumber.Value.ToString() : string.Empty;
    public string RightLineNumberText => Model.RightLineNumber.HasValue ? Model.RightLineNumber.Value.ToString() : string.Empty;

    [ObservableProperty]
    private string _leftText;

    [ObservableProperty]
    private string _rightText;

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

    public event Action? LeftTextEdited;
    public event Action? RightTextEdited;

    partial void OnLeftTextChanged(string value) => LeftTextEdited?.Invoke();
    partial void OnRightTextChanged(string value) => RightTextEdited?.Invoke();

    public IBrush LeftBackground => Model.LineType switch
    {
        LineType.Modified => ModifiedBrush,
        LineType.Deleted => OneSidedContentBrush,
        LineType.Filler => FillerBrush,
        _ => NormalBrush
    };

    public IBrush RightBackground => Model.LineType switch
    {
        LineType.Modified => ModifiedBrush,
        LineType.Added => OneSidedContentBrush,
        LineType.Filler => FillerBrush,
        _ => NormalBrush
    };

    /// <summary>Background of the changed words in the left text of a modified line.</summary>
    public IBrush? LeftWordHighlightBrush => Model.LineType == LineType.Modified
        ? (ThemeService.Instance.IsDark ? ChangedWordLeftModifiedBrushDark : ChangedWordLeftModifiedBrush)
        : null;

    /// <summary>Background of the changed words in the right text of a modified line.</summary>
    public IBrush? RightWordHighlightBrush => Model.LineType == LineType.Modified
        ? (ThemeService.Instance.IsDark ? ChangedWordRightModifiedBrushDark : ChangedWordRightModifiedBrush)
        : null;

    /// <summary>Word-level pieces of the left text (created on first use).</summary>
    public IReadOnlyList<SpanViewModel> LeftSpanViewModels =>
        _leftSpanViewModels ??= BuildSpanViewModels(Model.LeftSpans, Model.LineType, isLeft: true);

    /// <summary>Word-level pieces of the right text (created on first use).</summary>
    public IReadOnlyList<SpanViewModel> RightSpanViewModels =>
        _rightSpanViewModels ??= BuildSpanViewModels(Model.RightSpans, Model.LineType, isLeft: false);

    public bool HasSpans => Model.LeftSpans.Count > 1 || Model.RightSpans.Count > 1;

    public DiffLineViewModel(DiffLine model)
    {
        Model = model;
        _leftText = model.LeftText;
        _rightText = model.RightText;
    }

    /// <summary>Notifies the view that all brush-bound properties have changed (called on theme toggle).</summary>
    public void NotifyThemeChanged()
    {
        OnPropertyChanged(LeftBackgroundChangedArgs);
        OnPropertyChanged(RightBackgroundChangedArgs);
        if (Model.LineType == LineType.Modified)
        {
            OnPropertyChanged(LeftWordHighlightChangedArgs);
            OnPropertyChanged(RightWordHighlightChangedArgs);
        }
    }

    private static IReadOnlyList<SpanViewModel> BuildSpanViewModels(IReadOnlyList<DiffSpan> spans, LineType lineType, bool isLeft)
    {
        if (spans.Count == 0)
            return Array.Empty<SpanViewModel>();

        IBrush changedBrush = lineType switch
        {
            LineType.Modified when isLeft => ChangedWordLeftModifiedBrush,
            LineType.Modified => ChangedWordRightModifiedBrush,
            LineType.Added => ChangedWordAddedBrush,
            LineType.Deleted => ChangedWordDeletedBrush,
            _ => Brushes.Transparent
        };

        var result = new SpanViewModel[spans.Count];
        for (int i = 0; i < spans.Count; i++)
        {
            result[i] = new SpanViewModel
            {
                Text = spans[i].Text,
                Background = spans[i].IsChanged ? changedBrush : Brushes.Transparent
            };
        }
        return result;
    }
}
