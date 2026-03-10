using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Codiff.Controls;
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
        InitializeScrollSync();
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

    private void InitializeScrollSync()
    {
        _leftScrollViewer = this.FindControl<ScrollViewer>("LeftScrollViewer");
        _rightScrollViewer = this.FindControl<ScrollViewer>("RightScrollViewer");
        _leftDiffMap = this.FindControl<DiffMapControl>("LeftDiffMap");
        _rightDiffMap = this.FindControl<DiffMapControl>("RightDiffMap");

        if (_leftScrollViewer != null)
            _leftScrollViewer.ScrollChanged += OnLeftScrollChanged;

        if (_rightScrollViewer != null)
            _rightScrollViewer.ScrollChanged += OnRightScrollChanged;

        if (_leftDiffMap != null)
            _leftDiffMap.TargetScrollViewer = _leftScrollViewer;

        if (_rightDiffMap != null)
            _rightDiffMap.TargetScrollViewer = _rightScrollViewer;

        if (DataContext is FileDiffViewModel vm)
            SubscribeToViewModel(vm);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is FileDiffViewModel vm)
            SubscribeToViewModel(vm);
    }

    private void SubscribeToViewModel(FileDiffViewModel vm)
    {
        vm.PropertyChanged -= OnViewModelPropertyChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileDiffViewModel.ScrollOffset))
        {
            if (sender is FileDiffViewModel vm)
                ScrollToOffset(vm.ScrollOffset);
        }
    }

    private void ScrollToOffset(double offset)
    {
        _isSyncing = true;
        try
        {
            _leftScrollViewer?.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_leftScrollViewer.Offset.X, offset));
            _rightScrollViewer?.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_rightScrollViewer?.Offset.X ?? 0, offset));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnLeftScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing || _rightScrollViewer == null || _leftScrollViewer == null) return;
        if (e.OffsetDelta.Y == 0 && e.OffsetDelta.X == 0) return;

        _isSyncing = true;
        try
        {
            _rightScrollViewer.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_rightScrollViewer.Offset.X, _leftScrollViewer.Offset.Y));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnRightScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing || _leftScrollViewer == null || _rightScrollViewer == null) return;
        if (e.OffsetDelta.Y == 0 && e.OffsetDelta.X == 0) return;

        _isSyncing = true;
        try
        {
            _leftScrollViewer.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(_leftScrollViewer.Offset.X, _rightScrollViewer.Offset.Y));
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
