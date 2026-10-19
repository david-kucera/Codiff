using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Codiff.Models;
using Codiff.Services;
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

    // Light-theme brushes (as GitHub: deleted red, added green, changed amber)
    private static readonly IBrush ModifiedBrushLight = new SolidColorBrush(Color.Parse("#D4A72C"));
    private static readonly IBrush AddedBrushLight = new SolidColorBrush(Color.Parse("#4AC26B"));
    private static readonly IBrush DeletedBrushLight = new SolidColorBrush(Color.Parse("#E5534B"));
    private static readonly IBrush BackgroundBrushLight = new SolidColorBrush(Color.Parse("#F0F0F0"));
    private static readonly IPen BorderPenLight = new Pen(new SolidColorBrush(Color.Parse("#DDDDDD")), 1);
    private static readonly IBrush ViewportBrushLight = new SolidColorBrush(Color.Parse("#22000000"));

    // Dark-theme brushes
    private static readonly IBrush ModifiedBrushDark = new SolidColorBrush(Color.Parse("#BB8009"));
    private static readonly IBrush AddedBrushDark = new SolidColorBrush(Color.Parse("#2EA043"));
    private static readonly IBrush DeletedBrushDark = new SolidColorBrush(Color.Parse("#DA3633"));
    private static readonly IBrush BackgroundBrushDark = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IPen BorderPenDark = new Pen(new SolidColorBrush(Color.Parse("#444444")), 1);
    private static readonly IBrush ViewportBrushDark = new SolidColorBrush(Color.Parse("#44FFFFFF"));

    private static IBrush ModifiedBrush => ThemeService.Instance.IsDark ? ModifiedBrushDark : ModifiedBrushLight;
    private static IBrush AddedBrush => ThemeService.Instance.IsDark ? AddedBrushDark : AddedBrushLight;
    private static IBrush DeletedBrush => ThemeService.Instance.IsDark ? DeletedBrushDark : DeletedBrushLight;
    private static IBrush BackgroundBrush => ThemeService.Instance.IsDark ? BackgroundBrushDark : BackgroundBrushLight;
    private static IPen BorderPen => ThemeService.Instance.IsDark ? BorderPenDark : BorderPenLight;
    private static IBrush ViewportBrush => ThemeService.Instance.IsDark ? ViewportBrushDark : ViewportBrushLight;

    // Consecutive changed lines of the same kind. Rendering runs on every scroll, so it draws one
    // rectangle per block instead of one per line; rebuilt only when the lines change.
    private readonly List<(int Start, int Count, LineType Type)> _runs = new();
    private bool _runsDirty = true;

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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LinesProperty)
        {
            if (change.OldValue is ObservableCollection<DiffLineViewModel> oldCollection)
                oldCollection.CollectionChanged -= OnLinesCollectionChanged;

            if (change.NewValue is ObservableCollection<DiffLineViewModel> newCollection)
                newCollection.CollectionChanged += OnLinesCollectionChanged;

            _runsDirty = true;
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
        _runsDirty = true;
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

    private void RebuildRuns(IList<DiffLineViewModel> lines)
    {
        _runs.Clear();
        int i = 0;
        while (i < lines.Count)
        {
            var lineType = lines[i].LineType;
            int start = i;
            while (i < lines.Count && lines[i].LineType == lineType)
                i++;

            if (lineType != LineType.Unchanged && lineType != LineType.Filler)
                _runs.Add((start, i - start, lineType));
        }
        _runsDirty = false;
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

        if (_runsDirty)
            RebuildRuns(lines);

        int totalLines = lines.Count;
        double markerMinHeight = 2.0;
        double lineHeight = height / totalLines;
        double markerHeight = Math.Max(lineHeight, markerMinHeight);

        foreach (var (start, count, lineType) in _runs)
        {
            IBrush brush = lineType switch
            {
                LineType.Modified => ModifiedBrush,
                LineType.Added => AddedBrush,
                LineType.Deleted => DeletedBrush,
                _ => Brushes.Transparent
            };

            // Covers exactly the area of the block's individual line markers.
            double y = (double)start / totalLines * height;
            double blockHeight = (count - 1) * lineHeight + markerHeight;
            context.DrawRectangle(brush, null, new Rect(1, y, width - 2, blockHeight));
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

