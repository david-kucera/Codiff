using Avalonia.Controls;
using Avalonia.Interactivity;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FolderDiffView : UserControl
{
    public FolderDiffView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
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
        if (DataContext is FolderDiffViewModel vm)
        {
            // ensure we are not subscribed multiple times
            vm.FolderOpened -= OnFolderOpened;
            vm.FolderOpened += OnFolderOpened;
        }
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
