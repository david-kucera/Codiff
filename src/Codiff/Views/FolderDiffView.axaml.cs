using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FolderDiffView : UserControl
{
    // Avalonia DataGrid exposes no public API to set the vertical scroll position.
    // The private SetVerticalOffset(double) method is the single place that scrolls
    // the rows presenter and keeps the scrollbar in sync.
    private static readonly MethodInfo? s_setVerticalOffset =
        typeof(DataGrid).GetMethod("SetVerticalOffset", BindingFlags.NonPublic | BindingFlags.Instance);

    private bool _isSyncing;
    private FolderDiffViewModel? _subscribedViewModel;

    public FolderDiffView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        if (s_setVerticalOffset == null)
            System.Diagnostics.Debug.WriteLine(
                "[FolderDiffView] WARNING: DataGrid.SetVerticalOffset not found. " +
                "Vertical scroll synchronisation will be disabled. " +
                "Check if the Avalonia.Controls.DataGrid package was updated.");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetupGrids();
    }

    // ── Path-label taps ────────────────────────────────────────────────────

    private void OnLeftPathDoubleTapped(object? sender, RoutedEventArgs e)
        => (DataContext as FolderDiffViewModel)?.OpenFolderInExplorer(isLeft: true);

    private void OnRightPathDoubleTapped(object? sender, RoutedEventArgs e)
        => (DataContext as FolderDiffViewModel)?.OpenFolderInExplorer(isLeft: false);

    // ── DataContext lifecycle ───────────────────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.FolderOpened -= OnFolderOpened;
            _subscribedViewModel.FileEntrySelected -= OnFileEntrySelected;
        }

        _subscribedViewModel = DataContext as FolderDiffViewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.FolderOpened += OnFolderOpened;
            _subscribedViewModel.FileEntrySelected += OnFileEntrySelected;
        }
    }

    // ── Grid wiring (row colours + scroll sync) ─────────────────────────────

    private void SetupGrids()
    {
        var leftGrid = this.FindControl<DataGrid>("LeftDataGrid");
        var rightGrid = this.FindControl<DataGrid>("RightDataGrid");
        if (leftGrid == null || rightGrid == null) return;

        // Per-row background colours ─────────────────────────────────────────
        leftGrid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is FolderNodeViewModel node)
                args.Row.Background = node.LeftStatusColor;
        };

        rightGrid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is FolderNodeViewModel node)
                args.Row.Background = node.RightStatusColor;
        };

        // Vertical scroll synchronisation ─────────────────────────────────────
        // Avalonia's DataGrid processes scrolling via ScrollBar.Scroll (a regular
        // C# event raised only by user-interaction handlers in ScrollBar), NOT via
        // ScrollBar.ValueChanged.  Setting scrollBar.Value programmatically never
        // fires Scroll, so the DataGrid rows presenter never moves.
        //
        // The correct fix is to call DataGrid.SetVerticalOffset(double) which is
        // the single private method that (a) clamps the value, (b) invalidates the
        // rows arrange, and (c) calls UpdateVerticalScrollBar() so the thumb
        // position stays in sync.  We detect changes via ValueChanged (fired when
        // the DataGrid itself updates the scrollbar after a user scroll) and apply
        // the same offset to the opposite grid.
        leftGrid.TemplateApplied += (_, args) =>
        {
            var leftVSb = args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
            if (leftVSb != null)
                leftVSb.ValueChanged += (_, _) =>
                {
                    if (_isSyncing) return;
                    _isSyncing = true;
                    try { s_setVerticalOffset?.Invoke(rightGrid, new object[] { leftVSb.Value }); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FolderDiffView] Scroll sync (L→R) failed: {ex.InnerException ?? ex}"); }
                    finally { _isSyncing = false; }
                };
        };

        rightGrid.TemplateApplied += (_, args) =>
        {
            var rightVSb = args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
            if (rightVSb != null)
                rightVSb.ValueChanged += (_, _) =>
                {
                    if (_isSyncing) return;
                    _isSyncing = true;
                    try { s_setVerticalOffset?.Invoke(leftGrid, new object[] { rightVSb.Value }); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FolderDiffView] Scroll sync (R→L) failed: {ex.InnerException ?? ex}"); }
                    finally { _isSyncing = false; }
                };
        };
    }

    // ── Double-click → open file diff ──────────────────────────────────────

    private void OnGridDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FolderDiffViewModel vm) return;
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not FolderNodeViewModel node) return;
        if (!node.IsFile) return; // Folders are expanded/collapsed via the toggle button

        vm.SelectedNode = node;
        vm.OpenSelectedNode();
    }

    // ── FolderOpened (expand node from outside) ────────────────────────────

    private void OnFolderOpened(FolderNodeViewModel openedNode)
    {
        // Setting IsExpanded triggers FolderDiffViewModel.OnNodePropertyChanged
        // which rebuilds FlatRows, making both DataGrids update automatically.
        openedNode.IsExpanded = true;
    }

    // ── FileEntrySelected (open file diff window) ──────────────────────────

    private void OnFileEntrySelected(string leftPath, string rightPath)
    {
        if (!IsPathSelectionSafe(leftPath, rightPath))
            return;

        var vm = new FileDiffViewModel();

        if (!string.IsNullOrEmpty(leftPath) && !string.IsNullOrEmpty(rightPath))
        {
            vm.LoadFiles(leftPath, rightPath);
        }
        else if (!string.IsNullOrEmpty(leftPath))
        {
            try
            {
                vm.LoadTexts(File.ReadAllText(leftPath), string.Empty, leftPath, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read left file: {leftPath}");
                System.Diagnostics.Debug.WriteLine(ex);
                return;
            }
        }
        else if (!string.IsNullOrEmpty(rightPath))
        {
            try
            {
                vm.LoadTexts(string.Empty, File.ReadAllText(rightPath), string.Empty, rightPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read right file: {rightPath}");
                System.Diagnostics.Debug.WriteLine(ex);
                return;
            }
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

    private bool IsPathSelectionSafe(string leftPath, string rightPath)
    {
        if (DataContext is not FolderDiffViewModel vm)
            return false;

        if (!string.IsNullOrEmpty(leftPath) &&
            !IsUnderFolder(leftPath, vm.LeftFolderPath))
            return false;

        if (!string.IsNullOrEmpty(rightPath) &&
            !IsUnderFolder(rightPath, vm.RightFolderPath))
            return false;

        return true;
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
            return false;

        var fullFilePath = Path.GetFullPath(filePath);
        if (!File.Exists(fullFilePath))
            return false;

        var fullFolderPath = Path.GetFullPath(folderPath);
        var relativePath = Path.GetRelativePath(fullFolderPath, fullFilePath);

        if (Path.IsPathRooted(relativePath))
            return false;

        return !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
