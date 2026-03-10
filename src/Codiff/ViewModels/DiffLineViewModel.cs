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
    public DiffLine Model { get; }

    public string LeftLineNumberText => Model.LeftLineNumber.HasValue ? Model.LeftLineNumber.Value.ToString() : string.Empty;
    public string RightLineNumberText => Model.RightLineNumber.HasValue ? Model.RightLineNumber.Value.ToString() : string.Empty;
    public string LeftText => Model.LeftText;
    public string RightText => Model.RightText;
    public LineType LineType => Model.LineType;

    public IBrush Background => Model.LineType switch
    {
        LineType.Modified => new SolidColorBrush(Color.Parse("#FFFACD")),
        LineType.Added => new SolidColorBrush(Color.Parse("#CCFFCC")),
        LineType.Deleted => new SolidColorBrush(Color.Parse("#FFCCCC")),
        LineType.Filler => new SolidColorBrush(Color.Parse("#EEEEEE")),
        _ => Brushes.White
    };

    public IReadOnlyList<SpanViewModel> LeftSpanViewModels { get; }
    public IReadOnlyList<SpanViewModel> RightSpanViewModels { get; }

    public bool HasSpans => LeftSpanViewModels.Count > 1 || RightSpanViewModels.Count > 1;

    public DiffLineViewModel(DiffLine model)
    {
        Model = model;
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
