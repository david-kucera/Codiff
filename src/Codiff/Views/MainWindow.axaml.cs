using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainWindowViewModel vm)
            return;

        if (vm.IsFolderViewVisible)
        {
            if (Shortcuts.Recompare.Matches(e))
            {
                if (vm.FolderDiffViewModel.RecompareCommand.CanExecute(null))
                    vm.FolderDiffViewModel.RecompareCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (FileView.HandleShortcut(e))
        {
            // The file view handles its shortcuts itself while it has the focus; this covers the
            // rest of the window (menu, toolbar).
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenFilesDialog = OpenFilesDialogAsync;
            vm.OpenFoldersDialog = OpenFoldersDialogAsync;
            vm.ExitApplication = Close;
            vm.ShowErrorAsync = message => MessageDialog.ShowAsync(this, "Codiff", message);

            vm.RecentComparisons.CollectionChanged += OnRecentComparisonsChanged;
            RebuildRecentMenuItems(vm);
        }
    }

    private void OnFindMenuItemClick(object? sender, RoutedEventArgs e) => FileView.ShowFindBar();

    private void OnGoToLineMenuItemClick(object? sender, RoutedEventArgs e) => FileView.ShowGoToLine();

    private void OnCopyUnifiedDiffMenuItemClick(object? sender, RoutedEventArgs e) => _ = FileView.CopyUnifiedDiffAsync();

    private void OnSaveUnifiedDiffMenuItemClick(object? sender, RoutedEventArgs e) => _ = FileView.SaveUnifiedDiffAsync();

    // ── Drag and drop ──────────────────────────────────────────────────────

    private static List<string> GetDroppedPaths(DragEventArgs e) =>
        e.Data.GetFiles()?
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .ToList() ?? [];

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDroppedPaths(e).Count is 1 or 2 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>
    /// Two dropped files or folders are compared with each other; a single one replaces the side
    /// of the current comparison it was dropped on.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var paths = GetDroppedPaths(e);
        if (paths.Count == 2)
            await vm.OpenComparisonAsync(paths[0], paths[1]);
        else if (paths.Count == 1)
            await vm.ReplaceSideAsync(paths[0], isLeft: e.GetPosition(this).X < Bounds.Width / 2);
    }

    // ── Recent comparisons menu ────────────────────────────────────────────

    private void OnRecentComparisonsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            RebuildRecentMenuItems(vm);
    }

    private void RebuildRecentMenuItems(MainWindowViewModel vm)
    {
        RecentMenuItem.Items.Clear();
        foreach (var recent in vm.RecentComparisons)
        {
            var menuItem = new MenuItem
            {
                Header = recent.DisplayText,
                Command = recent.OpenCommand
            };
            // Close the menu after the command executes
            menuItem.Click += (s, e) =>
            {
                // Close all open menus by setting the menu's IsOpen to false
                foreach (var item in MainMenu.Items.OfType<MenuItem>())
                {
                    item.IsSubMenuOpen = false;
                }
            };
            RecentMenuItem.Items.Add(menuItem);
        }
    }

    // ── Pickers ────────────────────────────────────────────────────────────

    private async Task<(string? left, string? right)> OpenFilesDialogAsync()
    {
        var leftFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Left File",
            AllowMultiple = false
        });
        if (leftFiles.Count == 0) return (null, null);

        var rightFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Right File",
            AllowMultiple = false
        });
        if (rightFiles.Count == 0) return (null, null);

        return (leftFiles[0].Path.LocalPath, rightFiles[0].Path.LocalPath);
    }

    private async Task<(string? left, string? right)> OpenFoldersDialogAsync()
    {
        var leftFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Left Folder",
            AllowMultiple = false
        });
        if (leftFolders.Count == 0) return (null, null);

        var rightFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Right Folder",
            AllowMultiple = false
        });
        if (rightFolders.Count == 0) return (null, null);

        return (leftFolders[0].Path.LocalPath, rightFolders[0].Path.LocalPath);
    }
}
