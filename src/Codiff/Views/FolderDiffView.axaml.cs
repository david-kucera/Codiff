using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FolderDiffView : UserControl
{
    private ScrollViewer? _leftScrollViewer;
    private ScrollViewer? _rightScrollViewer;
    private bool _isSyncing;
    private FolderDiffViewModel? _subscribedViewModel;

    public FolderDiffView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeScrollSync();
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is FolderDiffViewModel vm && sender is ListBox lb)
        {
            if (lb.SelectedItem is FolderEntryViewModel entry)
                vm.SelectEntryCommand.Execute(entry);
        }
    }

    private void OnLeftPathDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FolderDiffViewModel vm)
        {
            vm.OpenFolderInExplorer(isLeft: true);
        }
    }

    private void OnRightPathDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FolderDiffViewModel vm)
        {
            vm.OpenFolderInExplorer(isLeft: false);
        }
    }

    private void OnNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FolderDiffViewModel vm)
            return;

        if (sender is Control control && control.DataContext is FolderNodeViewModel node)
        {
            vm.SelectedNode = node;
            vm.OpenSelectedNode();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.FolderOpened -= OnFolderOpened;
            _subscribedViewModel.FileEntrySelected -= OnFileEntrySelected;
        }

        if (DataContext is FolderDiffViewModel vm)
        {
            vm.FolderOpened += OnFolderOpened;
            vm.FileEntrySelected += OnFileEntrySelected;
            _subscribedViewModel = vm;
        }
        else
        {
            _subscribedViewModel = null;
        }
    }

    private void InitializeScrollSync()
    {
        _leftScrollViewer = this.FindControl<ScrollViewer>("LeftScrollViewer");
        _rightScrollViewer = this.FindControl<ScrollViewer>("RightScrollViewer");

        if (_leftScrollViewer != null)
            _leftScrollViewer.ScrollChanged += OnLeftScrollChanged;

        if (_rightScrollViewer != null)
            _rightScrollViewer.ScrollChanged += OnRightScrollChanged;
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

    private void OnFileEntrySelected(string leftPath, string rightPath)
    {
        var vm = new FileDiffViewModel();

        if (!string.IsNullOrEmpty(leftPath) && !string.IsNullOrEmpty(rightPath))
        {
            vm.LoadFiles(leftPath, rightPath);
        }
        else if (!string.IsNullOrEmpty(leftPath))
        {
            vm.LoadTexts(File.ReadAllText(leftPath), string.Empty, leftPath, string.Empty);
        }
        else if (!string.IsNullOrEmpty(rightPath))
        {
            vm.LoadTexts(string.Empty, File.ReadAllText(rightPath), string.Empty, rightPath);
        }
        else
        {
            return;
        }

        var window = new Window
        {
            Title = "Codiff - File Compare",
            Width = 1280,
            Height = 960,
            MinWidth = 800,
            MinHeight = 500,
            Content = new FileDiffView
            {
                DataContext = vm
            }
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    private void OnFolderOpened(FolderNodeViewModel openedNode)
    {
        if (DataContext is not FolderDiffViewModel vm)
            return;

        var targetSide = openedNode.Side == FolderSide.Left ? FolderSide.Right : FolderSide.Left;

        var match = FindNodeByRelativePathAndSide(vm.RootNodes, openedNode.RelativePath, targetSide);
        if (match == null)
            return;

        var targetTree = targetSide == FolderSide.Left ? LeftTree : RightTree;
        targetTree.SelectedItem = match;
    }

    private static FolderNodeViewModel? FindNodeByRelativePathAndSide(
        IEnumerable<FolderNodeViewModel> roots,
        string relativePath,
        FolderSide side)
    {
        foreach (var root in roots)
        {
            var found = FindNodeRecursive(root, relativePath, side);
            if (found != null)
                return found;
        }

        return null;
    }

    private static FolderNodeViewModel? FindNodeRecursive(
        FolderNodeViewModel node,
        string relativePath,
        FolderSide side)
    {
        if (node.Side == side && string.Equals(node.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeRecursive(child, relativePath, side);
            if (found != null)
                return found;
        }

        return null;
    }
}
