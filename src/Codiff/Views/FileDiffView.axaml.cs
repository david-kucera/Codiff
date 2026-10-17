using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Codiff.Controls;
using Codiff.Services;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FileDiffView : UserControl
{
    /// <summary>Height of every diff row in pixels (rows have a fixed height so both sides stay aligned).</summary>
    public const double RowHeight = 20;

    // Must match the line text style in the row templates (FileDiffView.axaml).
    private static readonly Typeface LineTypeface = new("Consolas,Courier New,monospace");
    private const double LineFontSize = 13;

    // Line-number column + the TextBox's horizontal padding + room for the caret.
    private const double RowChromeWidth = 48 + 4 + 4 + 4;

    private ScrollViewer? _leftScrollViewer;
    private ScrollViewer? _rightScrollViewer;
    private DiffMapControl? _leftDiffMap;
    private DiffMapControl? _rightDiffMap;
    private bool _isSyncing;

    /// <summary>Vertical offset still to be applied once the scroll viewers have measured their content (NaN = none).</summary>
    private double _pendingScrollOffset = double.NaN;

    public FileDiffView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RewireScrollSync();
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // A view model loaded before this view existed (e.g. a popup window) has already asked
        // to show its first difference; do it now that there is something to scroll.
        if (DataContext is FileDiffViewModel { CurrentHunkLineIndex: >= 0 } vm)
            RequestScroll(vm.CurrentHunkLineIndex * RowHeight);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        if (DataContext is FileDiffViewModel vm)
        {
            foreach (var line in vm.Lines)
                line.NotifyThemeChanged();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is FileDiffViewModel vm)
            {
                vm.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnLeftPathPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FileDiffViewModel vm)
            vm.OpenInExplorerCommand.Execute(vm.LeftFilePath);
    }

    private void OnRightPathPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FileDiffViewModel vm)
            vm.OpenInExplorerCommand.Execute(vm.RightFilePath);
    }

    // ── Scroll sync ────────────────────────────────────────────────────────

    /// <summary>
    /// (Re-)wires the active scroll viewers based on the current split orientation.
    /// In vertical mode the Left/Right viewers are used; in horizontal mode Top/Bottom.
    /// </summary>
    private void RewireScrollSync()
    {
        // Unsubscribe from old viewers
        if (_leftScrollViewer != null) _leftScrollViewer.ScrollChanged -= OnLeftScrollChanged;
        if (_rightScrollViewer != null) _rightScrollViewer.ScrollChanged -= OnRightScrollChanged;

        bool isHorizontal = DataContext is FileDiffViewModel { IsHorizontalSplit: true };

        if (isHorizontal)
        {
            _leftScrollViewer  = this.FindControl<ScrollViewer>("TopScrollViewer");
            _rightScrollViewer = this.FindControl<ScrollViewer>("BottomScrollViewer");
            _leftDiffMap  = this.FindControl<DiffMapControl>("TopDiffMap");
            _rightDiffMap = this.FindControl<DiffMapControl>("BottomDiffMap");
        }
        else
        {
            _leftScrollViewer  = this.FindControl<ScrollViewer>("LeftScrollViewer");
            _rightScrollViewer = this.FindControl<ScrollViewer>("RightScrollViewer");
            _leftDiffMap  = this.FindControl<DiffMapControl>("LeftDiffMap");
            _rightDiffMap = this.FindControl<DiffMapControl>("RightDiffMap");
        }

        if (_leftScrollViewer != null)  _leftScrollViewer.ScrollChanged  += OnLeftScrollChanged;
        if (_rightScrollViewer != null) _rightScrollViewer.ScrollChanged += OnRightScrollChanged;

        if (_leftDiffMap != null)  _leftDiffMap.TargetScrollViewer  = _leftScrollViewer;
        if (_rightDiffMap != null) _rightDiffMap.TargetScrollViewer = _rightScrollViewer;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SubscribeToViewModel(DataContext as FileDiffViewModel);
    }

    private FileDiffViewModel? _subscribedViewModel;

    private void SubscribeToViewModel(FileDiffViewModel? vm)
    {
        if (vm == _subscribedViewModel) return;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.CopyAsTextRequested -= OnCopyAsTextRequested;
            _subscribedViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            _subscribedViewModel.Lines.CollectionChanged -= OnLinesChanged;
        }

        _subscribedViewModel = vm;

        if (vm != null)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.CopyAsTextRequested += OnCopyAsTextRequested;
            vm.ScrollToLineRequested += OnScrollToLineRequested;
            vm.Lines.CollectionChanged += OnLinesChanged;
        }

        UpdateContentWidth();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileDiffViewModel.IsHorizontalSplit))
        {
            // Keep showing the same lines in the newly visible pair of panels.
            double offset = _leftScrollViewer?.Offset.Y ?? 0;
            RewireScrollSync();
            RequestScroll(offset);
        }
    }

    private void OnScrollToLineRequested(int lineIndex) => RequestScroll(lineIndex * RowHeight);

    /// <summary>
    /// Scrolls both sides to <paramref name="verticalOffset"/>. Right after new lines are loaded
    /// (or the panels become visible) the viewers haven't measured them yet and would clamp the
    /// offset to the old content, so it is applied again once layout has run.
    /// </summary>
    private void RequestScroll(double verticalOffset)
    {
        ScrollToOffset(verticalOffset);
        if (_leftScrollViewer != null && Math.Abs(_leftScrollViewer.Offset.Y - verticalOffset) < 0.5)
        {
            _pendingScrollOffset = double.NaN;
            return;
        }

        _pendingScrollOffset = verticalOffset;
        Dispatcher.UIThread.Post(ApplyPendingScroll, DispatcherPriority.Loaded);
    }

    private void ApplyPendingScroll()
    {
        if (double.IsNaN(_pendingScrollOffset))
            return;

        ScrollToOffset(_pendingScrollOffset);

        // Retried only once, so a stale request never overrides where the user scrolls later.
        _pendingScrollOffset = double.NaN;
    }

    private void ScrollToOffset(double verticalOffset)
    {
        _isSyncing = true;
        try
        {
            _leftScrollViewer?.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_leftScrollViewer.Offset.X, verticalOffset));
            _rightScrollViewer?.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_rightScrollViewer?.Offset.X ?? 0, verticalOffset));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnLeftScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing || _rightScrollViewer == null || _leftScrollViewer == null)
            return;
        if (e.OffsetDelta.Y == 0 && e.OffsetDelta.X == 0)
            return;

        _isSyncing = true;
        try
        {
            // Keep both horizontal and vertical scroll positions in sync.
            _rightScrollViewer.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_leftScrollViewer.Offset.X, _leftScrollViewer.Offset.Y));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnRightScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing || _leftScrollViewer == null || _rightScrollViewer == null)
            return;
        if (e.OffsetDelta.Y == 0 && e.OffsetDelta.X == 0)
            return;

        _isSyncing = true;
        try
        {
            // Keep both horizontal and vertical scroll positions in sync.
            _leftScrollViewer.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_rightScrollViewer.Offset.X, _rightScrollViewer.Offset.Y));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    // ── Content width ──────────────────────────────────────────────────────

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateContentWidth();

    /// <summary>
    /// Gives all four line lists the width of the widest line (of either file). Rows are
    /// virtualized, so otherwise the horizontal extent would only cover the rows currently in
    /// view and change while scrolling; a common width also lets both sides scroll horizontally
    /// in sync even when one file has longer lines.
    /// </summary>
    private void UpdateContentWidth()
    {
        double width = 0;
        if (_subscribedViewModel is { Lines.Count: > 0 } vm)
            width = RowChromeWidth + MeasureWidestLine(vm.Lines);

        foreach (var name in new[] { "LeftLines", "RightLines", "TopLines", "BottomLines" })
        {
            if (this.FindControl<ItemsControl>(name) is { } list)
                list.MinWidth = width;
        }
    }

    private static double MeasureWidestLine(IReadOnlyList<DiffLineViewModel> lines)
    {
        // The font is monospaced, so it is enough to measure the few longest lines (tabs, whose
        // width depends on the position, are counted generously when picking them).
        const int CandidateCount = 4;
        var candidates = new List<(int Score, string Text)>(CandidateCount + 1);

        void Consider(string text)
        {
            if (text.Length == 0)
                return;
            int score = text.Length + 7 * text.AsSpan().Count('\t');
            if (candidates.Count == CandidateCount && score <= candidates[^1].Score)
                return;

            int index = candidates.FindIndex(c => c.Score < score);
            candidates.Insert(index < 0 ? candidates.Count : index, (score, text));
            if (candidates.Count > CandidateCount)
                candidates.RemoveAt(CandidateCount);
        }

        foreach (var line in lines)
        {
            Consider(line.LeftText);
            Consider(line.RightText);
        }

        double widest = 0;
        foreach (var (_, text) in candidates)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LineTypeface, LineFontSize, null);
            widest = Math.Max(widest, formatted.WidthIncludingTrailingWhitespace);
        }
        return Math.Ceiling(widest);
    }

    // ── Clipboard ──────────────────────────────────────────────────────────

    private async void OnCopyAsTextRequested(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
