using Avalonia.Controls;
using Avalonia.Interactivity;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FolderDiffView : UserControl
{
    public FolderDiffView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is FolderDiffViewModel vm && sender is ListBox lb)
        {
            if (lb.SelectedItem is FolderEntryViewModel entry)
                vm.SelectEntryCommand.Execute(entry);
        }
    }
}
