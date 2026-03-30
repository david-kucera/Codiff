using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Codiff.Models;
using Codiff.ViewModels;

namespace Codiff.Controls;

public class DiffMapControl : Control
{
    public static readonly StyledProperty<ObservableCollection<DiffLineViewModel>?> LinesProperty =
        AvaloniaProperty.Register<DiffMapControl, ObservableCollection<DiffLineViewModel>?>(nameof(Lines));

    public static readonly StyledProperty<ScrollViewer?> TargetScrollViewerProperty =
        AvaloniaProperty.Register<DiffMapControl, ScrollViewer?>(nameof(TargetScrollViewer));

    public static readonly StyledProperty<bool> IsLeftSideProperty =
        AvaloniaProperty.Register<DiffMapControl, bool>(nameof(IsLeftSide), true);

    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#CCAA00"));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.Parse("#22AA22"));
    private static readonly IBrush DeletedBrush = new SolidColorBrush(Color.Parse("#DD4444"));
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#F0F0F0"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#DDDDDD")), 1);
    private static readonly IBrush ViewportBrush = new SolidColorBrush(Color.Parse("#22000000"));

    public ObservableCollection<DiffLineViewModel>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public ScrollViewer? TargetScrollViewer
    {
        get => GetValue(TargetScrollViewerProperty);
        set => SetValue(TargetScrollViewerProperty, value);
    }

    public bool IsLeftSide
    {
        get => GetValue(IsLeftSideProperty);
        set => SetValue(IsLeftSideProperty, value);
    }

    static DiffMapControl()
    {
        AffectsRender<DiffMapControl>(LinesProperty);
        AffectsRender<DiffMapControl>(IsLeftSideProperty);
    }

    public DiffMapControl()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LinesProperty)
        {
            if (change.OldValue is ObservableCollection<DiffLineViewModel> oldCollection)
                oldCollection.CollectionChanged -= OnLinesCollectionChanged;

            if (change.NewValue is ObservableCollection<DiffLineViewModel> newCollection)
                newCollection.CollectionChanged += OnLinesCollectionChanged;

            InvalidateVisual();
        }

        if (change.Property == TargetScrollViewerProperty)
        {
            if (change.OldValue is ScrollViewer oldSv)
                oldSv.ScrollChanged -= OnTargetScrollChanged;

            if (change.NewValue is ScrollViewer newSv)
                newSv.ScrollChanged += OnTargetScrollChanged;

            InvalidateVisual();
        }
    }

    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnTargetScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ScrollToPointerPosition(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ScrollToPointerPosition(e);
            e.Handled = true;
        }
    }

    private void ScrollToPointerPosition(PointerEventArgs e)
    {
        var sv = TargetScrollViewer;
        var lines = Lines;
        if (sv == null || lines == null || lines.Count == 0) return;

        var pos = e.GetPosition(this);
        var ratio = Math.Clamp(pos.Y / Bounds.Height, 0, 1);
        var maxScroll = sv.Extent.Height - sv.Viewport.Height;
        if (maxScroll > 0)
        {
            var targetOffset = ratio * maxScroll;
            sv.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(sv.Offset.X, targetOffset));
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        context.DrawRectangle(BackgroundBrush, BorderPen, new Rect(0, 0, width, height));

        var lines = Lines;
        if (lines == null || lines.Count == 0) return;

        int totalLines = lines.Count;
        double markerMinHeight = 2.0;

        for (int i = 0; i < totalLines; i++)
        {
            var lineType = lines[i].LineType;
            if (lineType == LineType.Unchanged || lineType == LineType.Filler)
                continue;

            IBrush brush = lineType switch
            {
                LineType.Modified => ModifiedBrush,
                LineType.Added => IsLeftSide ? DeletedBrush : AddedBrush,
                LineType.Deleted => IsLeftSide ? AddedBrush : DeletedBrush,
                _ => Brushes.Transparent
            };

            double y = (double)i / totalLines * height;
            double markerHeight = Math.Max(height / totalLines, markerMinHeight);
            context.DrawRectangle(brush, null, new Rect(1, y, width - 2, markerHeight));
        }

        // Draw viewport indicator
        var sv = TargetScrollViewer;
        if (sv != null && sv.Extent.Height > 0)
        {
            double viewportRatio = sv.Viewport.Height / sv.Extent.Height;
            double offsetRatio = sv.Offset.Y / sv.Extent.Height;

            double vpY = offsetRatio * height;
            double vpH = Math.Max(viewportRatio * height, 8);
            vpY = Math.Min(vpY, height - vpH);

            context.DrawRectangle(ViewportBrush, null, new Rect(0, vpY, width, vpH));
        }
    }
}
