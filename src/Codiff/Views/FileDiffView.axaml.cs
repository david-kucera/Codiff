using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codiff.Controls;
using Codiff.Services;
using Codiff.ViewModels;
using CommunityToolkit.Mvvm.Input;

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

    /// <summary>Side of the pane that last had the keyboard focus (Find and Go to Line use it).</summary>
    private bool _activeSideIsLeft = true;

    /// <summary>Line of the last search match (-1 = none) and the line showing its highlight.</summary>
    private int _findLineIndex = -1;
    private DiffLineViewModel? _findMatchLine;

    public FileDiffView()
    {
        InitializeComponent();

        // Shortcuts are handled before the line TextBoxes get the keys (tunnel phase).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble);

        foreach (var list in new[] { LeftLines, RightLines, TopLines, BottomLines })
            list.AddHandler(RequestBringIntoViewEvent, OnLineRequestBringIntoView);
    }

    /// <summary>
    /// Clicking into a line asks to bring the whole (very wide) line TextBox into view, which would
    /// scroll the pane sideways so that the line numbers disappear. Keep the horizontal position
    /// for such requests and only scroll vertically. Small requests (the caret while typing) are
    /// followed, but when the caret fits into the first screen the pane scrolls fully left.
    /// </summary>
    private void OnLineRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not ItemsControl list || e.TargetObject is not Visual target)
            return;
        var viewer = list.FindAncestorOfType<ScrollViewer>();
        if (viewer == null || target.TranslatePoint(default, list) is not { } origin)
            return;

        if (e.TargetRect.Width < viewer.Viewport.Width / 2)
        {
            double right = origin.X + e.TargetRect.Right;
            if (right <= viewer.Viewport.Width)
                e.TargetRect = new Rect(-origin.X, e.TargetRect.Y, right, e.TargetRect.Height);
            return;
        }

        double x = Math.Clamp(viewer.Offset.X - origin.X, 0, Math.Max(0, e.TargetRect.Width - 1));
        e.TargetRect = new Rect(x, e.TargetRect.Y, 1, e.TargetRect.Height);
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

    // ── Keyboard ───────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleShortcut(e))
            e.Handled = true;
    }

    /// <summary>
    /// Runs the file comparison command bound to the pressed keys and returns true when there was
    /// one. The main window forwards keys here when the focus is outside this view.
    /// </summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (DataContext is not FileDiffViewModel vm)
            return false;

        if (e.Key == Key.Escape && FindBar.IsVisible)
        {
            CloseFindBar();
            return true;
        }

        // Ctrl+S is also accepted on macOS (Command+S is the native gesture there).
        if (Shortcuts.Save.Matches(e) || (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control))
            return Run(vm.SaveCommand);
        if (Shortcuts.NextDifference.Matches(e) || Shortcuts.NextDifferenceAlt.Matches(e))
            return Run(vm.NextDiffCommand);
        if (Shortcuts.PreviousDifference.Matches(e) || Shortcuts.PreviousDifferenceAlt.Matches(e))
            return Run(vm.PrevDiffCommand);
        if (Shortcuts.FirstDifference.Matches(e))
            return Run(vm.FirstDiffCommand);
        if (Shortcuts.LastDifference.Matches(e))
            return Run(vm.LastDiffCommand);
        if (Shortcuts.Recompare.Matches(e))
            return Run(vm.RecompareCommand);
        if (Shortcuts.CopyToRight.Matches(e))
            return Run(vm.CopyToRightCommand);
        if (Shortcuts.CopyToLeft.Matches(e))
            return Run(vm.CopyToLeftCommand);

        // Inside a text field the gesture undoes typing there instead.
        if (Shortcuts.Undo.Matches(e) && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
            return Run(vm.UndoCommand);

        if (Shortcuts.Find.Matches(e))
        {
            ShowFindBar();
            return true;
        }
        if (Shortcuts.FindNext.Matches(e) || Shortcuts.FindPrevious.Matches(e))
        {
            FindNext(forward: Shortcuts.FindNext.Matches(e));
            return true;
        }
        if (Shortcuts.GoToLine.Matches(e))
        {
            ShowGoToLine();
            return true;
        }
        return false;
    }

    private static bool Run(IRelayCommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
        return true;
    }

    /// <summary>Remembers which pane was used last (for Find and Go to Line).</summary>
    private void OnDescendantGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is not TextBox box)
            return;

        var list = box.FindAncestorOfType<ItemsControl>();
        if (list?.Name is "LeftLines" or "TopLines")
            _activeSideIsLeft = true;
        else if (list?.Name is "RightLines" or "BottomLines")
            _activeSideIsLeft = false;
    }

    // ── Find ───────────────────────────────────────────────────────────────

    private void OnFindButtonClick(object? sender, RoutedEventArgs e) => ShowFindBar();

    /// <summary>Shows the find bar, searching the pane that was used last.</summary>
    public void ShowFindBar()
    {
        if (!FindBar.IsVisible)
            FindSideComboBox.SelectedIndex = _activeSideIsLeft ? 0 : 1;
        FindBar.IsVisible = true;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void CloseFindBar()
    {
        FindBar.IsVisible = false;
        FindStatus.Text = string.Empty;
        ClearFindMatch();
        _findLineIndex = -1;
        Focus();
    }

    private void OnFindTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e) => FindNext(forward: true);

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e) => FindNext(forward: false);

    private void OnFindCloseClick(object? sender, RoutedEventArgs e) => CloseFindBar();

    /// <summary>Moves to the next (or previous) line containing the searched text and highlights the match.</summary>
    private void FindNext(bool forward)
    {
        if (DataContext is not FileDiffViewModel vm)
            return;

        var text = FindTextBox.Text;
        if (!FindBar.IsVisible || string.IsNullOrEmpty(text))
        {
            ShowFindBar();
            return;
        }

        bool isLeft = FindSideComboBox.SelectedIndex != 1;
        // Without a previous match, start at the first line in view.
        int from = _findLineIndex >= 0 && _findLineIndex < vm.Lines.Count
            ? _findLineIndex
            : FirstVisibleLineIndex() - (forward ? 1 : 0);
        var (lineIndex, position) = vm.Find(text, isLeft, from, forward, FindMatchCaseCheckBox.IsChecked == true);

        ClearFindMatch();
        if (lineIndex < 0)
        {
            FindStatus.Text = "Not found";
            return;
        }

        FindStatus.Text = string.Empty;
        _findLineIndex = lineIndex;
        _findMatchLine = vm.Lines[lineIndex];
        _findMatchLine.MatchLength = text.Length;
        if (isLeft)
            _findMatchLine.LeftMatchStart = position;
        else
            _findMatchLine.RightMatchStart = position;

        RevealLine(lineIndex);
    }

    private void ClearFindMatch()
    {
        if (_findMatchLine == null)
            return;
        _findMatchLine.LeftMatchStart = -1;
        _findMatchLine.RightMatchStart = -1;
        _findMatchLine.MatchLength = 0;
        _findMatchLine = null;
    }

    private int FirstVisibleLineIndex() =>
        _leftScrollViewer == null ? 0 : (int)Math.Ceiling(_leftScrollViewer.Offset.Y / RowHeight);

    /// <summary>Scrolls a line into view (a third from the top) unless it is visible already.</summary>
    private void RevealLine(int lineIndex)
    {
        var viewer = _leftScrollViewer;
        if (viewer == null)
            return;

        double top = lineIndex * RowHeight;
        if (top >= viewer.Offset.Y && top + RowHeight <= viewer.Offset.Y + viewer.Viewport.Height)
            return;
        RequestScroll(Math.Max(0, top - viewer.Viewport.Height / 3));
    }

    // ── Go to line ─────────────────────────────────────────────────────────

    /// <summary>Opens the Go to Line box for the pane that was used last.</summary>
    public void ShowGoToLine() => GoToLineButton.Flyout?.ShowAt(GoToLineButton);

    private void OnGoToLineFlyoutOpened(object? sender, EventArgs e)
    {
        GoToLeftRadio.IsChecked = _activeSideIsLeft;
        GoToRightRadio.IsChecked = !_activeSideIsLeft;
        GoToLineMessage.Text = string.Empty;
        Dispatcher.UIThread.Post(() =>
        {
            GoToLineTextBox.Focus();
            GoToLineTextBox.SelectAll();
        });
    }

    private void OnGoToLineTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GoToLine();
            e.Handled = true;
        }
    }

    private void OnGoToLineClick(object? sender, RoutedEventArgs e) => GoToLine();

    private void GoToLine()
    {
        if (DataContext is not FileDiffViewModel vm)
            return;

        if (!int.TryParse(GoToLineTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber) || lineNumber < 1)
        {
            GoToLineMessage.Text = "Enter a line number.";
            return;
        }
        if (!vm.GoToLine(lineNumber, isLeft: GoToRightRadio.IsChecked != true))
        {
            GoToLineMessage.Text = "There is no such line.";
            return;
        }
        GoToLineButton.Flyout?.Hide();
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

    // ── Unified diff export ────────────────────────────────────────────────

    private void OnCopyUnifiedDiffClick(object? sender, RoutedEventArgs e) => _ = CopyUnifiedDiffAsync();

    private void OnSaveUnifiedDiffClick(object? sender, RoutedEventArgs e) => _ = SaveUnifiedDiffAsync();

    /// <summary>Copies the differences as a unified diff to the clipboard.</summary>
    public async Task CopyUnifiedDiffAsync()
    {
        var diff = await GetUnifiedDiffOrExplainAsync();
        if (diff != null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(diff);
    }

    /// <summary>Saves the differences as a unified diff (.diff/.patch) file.</summary>
    public async Task SaveUnifiedDiffAsync()
    {
        var diff = await GetUnifiedDiffOrExplainAsync();
        if (diff == null || TopLevel.GetTopLevel(this) is not { } topLevel || DataContext is not FileDiffViewModel vm)
            return;

        var name = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(vm.LeftFilePath) ? vm.RightFilePath : vm.LeftFilePath);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Differences as Unified Diff",
            SuggestedFileName = (string.IsNullOrEmpty(name) ? "differences" : name) + ".diff",
            DefaultExtension = "diff",
            FileTypeChoices = [new FilePickerFileType("Unified diff") { Patterns = ["*.diff", "*.patch"] }]
        });
        if (file == null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(diff);
    }

    /// <summary>The unified diff, or null after telling the user why there is none.</summary>
    private async Task<string?> GetUnifiedDiffOrExplainAsync()
    {
        if (DataContext is not FileDiffViewModel vm)
            return null;

        var diff = vm.CreateUnifiedDiff();
        if (diff.Length > 0)
            return diff;

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            await MessageDialog.ShowAsync(owner, "Export Differences", vm.IsBinary
                ? "Binary files can't be exported as a unified diff."
                : "There are no differences to export.");
        }
        return null;
    }

    // ── Clipboard ──────────────────────────────────────────────────────────

    private async void OnCopyAsTextRequested(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
