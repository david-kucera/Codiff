using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Codiff.Controls;
using Codiff.Services;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FileDiffView : UserControl
{
    private ScrollViewer? _leftScrollViewer;
    private ScrollViewer? _rightScrollViewer;
    private DiffMapControl? _leftDiffMap;
    private DiffMapControl? _rightDiffMap;
    private bool _isSyncing;

    public FileDiffView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RewireScrollSync();
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
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

        if (DataContext is FileDiffViewModel vm)
            SubscribeToViewModel(vm);
    }

    private FileDiffViewModel? _subscribedViewModel;

    private void SubscribeToViewModel(FileDiffViewModel vm)
    {
        if (vm == _subscribedViewModel) return;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.CopyAsTextRequested -= OnCopyAsTextRequested;
        }

        _subscribedViewModel = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.CopyAsTextRequested += OnCopyAsTextRequested;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileDiffViewModel.ScrollOffset))
        {
            if (sender is FileDiffViewModel vm)
                ScrollToOffset(vm.ScrollOffset);
        }
        else if (e.PropertyName == nameof(FileDiffViewModel.IsHorizontalSplit))
        {
            RewireScrollSync();
        }
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

    // ── Clipboard ──────────────────────────────────────────────────────────

    private async void OnCopyAsTextRequested(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}

