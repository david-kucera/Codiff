using Avalonia.Media;
using Codiff.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Codiff.ViewModels;

public class SpanViewModel
{
    public string Text { get; set; } = string.Empty;
    public IBrush Background { get; set; } = Brushes.Transparent;
}

public partial class DiffLineViewModel : ObservableObject
{
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#FFFACD"));
    private static readonly IBrush PresentBrush = new SolidColorBrush(Color.Parse("#CCFFCC"));
    private static readonly IBrush FillerBrush = new SolidColorBrush(Color.Parse("#EEEEEE"));

    public DiffLine Model { get; }

    public string LeftLineNumberText => Model.LeftLineNumber.HasValue ? Model.LeftLineNumber.Value.ToString() : string.Empty;
    public string RightLineNumberText => Model.RightLineNumber.HasValue ? Model.RightLineNumber.Value.ToString() : string.Empty;

    [ObservableProperty]
    private string _leftText;

    [ObservableProperty]
    private string _rightText;

    public LineType LineType => Model.LineType;

    public event Action? LeftTextEdited;
    public event Action? RightTextEdited;

    partial void OnLeftTextChanged(string value) => LeftTextEdited?.Invoke();
    partial void OnRightTextChanged(string value) => RightTextEdited?.Invoke();

    public IBrush Background => LeftBackground;

    public IBrush LeftBackground => Model.LineType switch
    {
        LineType.Modified => ModifiedBrush,
        LineType.Deleted => PresentBrush,
        LineType.Filler => FillerBrush,
        _ => Brushes.White
    };

    public IBrush RightBackground => Model.LineType switch
    {
        LineType.Modified => ModifiedBrush,
        LineType.Added => PresentBrush,
        LineType.Filler => FillerBrush,
        _ => Brushes.White
    };

    public IReadOnlyList<SpanViewModel> LeftSpanViewModels { get; }
    public IReadOnlyList<SpanViewModel> RightSpanViewModels { get; }

    public bool HasSpans => LeftSpanViewModels.Count > 1 || RightSpanViewModels.Count > 1;

    public DiffLineViewModel(DiffLine model)
    {
        Model = model;
        _leftText = model.LeftText;
        _rightText = model.RightText;
        LeftSpanViewModels = BuildSpanViewModels(model.LeftSpans, model.LineType, isLeft: true);
        RightSpanViewModels = BuildSpanViewModels(model.RightSpans, model.LineType, isLeft: false);
    }

    private static IReadOnlyList<SpanViewModel> BuildSpanViewModels(IReadOnlyList<DiffSpan> spans, LineType lineType, bool isLeft)
    {
        IBrush changedBrush = lineType switch
        {
            LineType.Modified when isLeft => new SolidColorBrush(Color.Parse("#FFB347")),
            LineType.Modified => new SolidColorBrush(Color.Parse("#66CC66")),
            LineType.Added => new SolidColorBrush(Color.Parse("#88EE88")),
            LineType.Deleted => new SolidColorBrush(Color.Parse("#FF9999")),
            _ => Brushes.Transparent
        };

        var result = new List<SpanViewModel>();
        foreach (var span in spans)
        {
            result.Add(new SpanViewModel
            {
                Text = span.Text,
                Background = span.IsChanged ? changedBrush : Brushes.Transparent
            });
        }
        return result;
    }
}
