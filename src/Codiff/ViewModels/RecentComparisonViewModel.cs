using Codiff.Models;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

/// <summary>
/// View-model for a single recent-comparison entry shown in the "Recent Files/Directories" menu.
/// </summary>
public class RecentComparisonViewModel
{
    /// <summary>True for a folder comparison; false for a file comparison.</summary>
    public bool IsFolder { get; }

    public string LeftPath { get; }
    public string RightPath { get; }

    /// <summary>Short human-readable label: "leftName ↔ rightName".</summary>
    public string DisplayText { get; }

    public RelayCommand OpenCommand { get; }

    public RecentComparisonViewModel(RecentComparison model, Action<RecentComparisonViewModel> openAction)
    {
        IsFolder = model.IsFolder;
        LeftPath = model.LeftPath;
        RightPath = model.RightPath;
        DisplayText = BuildDisplayText(model.LeftPath, model.RightPath);
        OpenCommand = new RelayCommand(() => openAction(this));
    }

    private static string BuildDisplayText(string leftPath, string rightPath)
    {
        var left = Path.GetFileName(leftPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var right = Path.GetFileName(rightPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        // Fall back to the full path for root directories (e.g. "C:\" or "/")
        if (string.IsNullOrEmpty(left)) left = leftPath;
        if (string.IsNullOrEmpty(right)) right = rightPath;
        return $"{left} ↔ {right}";
    }
}
