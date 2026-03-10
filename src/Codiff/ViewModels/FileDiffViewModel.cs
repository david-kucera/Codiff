using System.Collections.ObjectModel;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FileDiffViewModel : ObservableObject
{
    private readonly FileDiffService _diffService = new();

    [ObservableProperty]
    private ObservableCollection<DiffLineViewModel> _lines = new();

    [ObservableProperty]
    private string _leftFilePath = string.Empty;

    [ObservableProperty]
    private string _rightFilePath = string.Empty;

    [ObservableProperty]
    private int _currentDiffIndex = -1;

    [ObservableProperty]
    private int _totalDiffs;

    [ObservableProperty]
    private double _scrollOffset;

    private IReadOnlyList<int> _hunkIndices = Array.Empty<int>();

    public string DiffCounterText => TotalDiffs > 0
        ? $"{(CurrentDiffIndex >= 0 ? CurrentDiffIndex + 1 : 0)} / {TotalDiffs}"
        : "No diffs";

    public bool HasDiffs => TotalDiffs > 0;

    public void LoadFiles(string leftPath, string rightPath)
    {
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffFiles(leftPath, rightPath);
        ApplyResult(result);
    }

    public void LoadTexts(string leftText, string rightText, string leftPath = "", string rightPath = "")
    {
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffTexts(leftText, rightText, leftPath, rightPath);
        ApplyResult(result);
    }

    private void ApplyResult(FileDiffResult result)
    {
        _hunkIndices = result.DiffHunkIndices;
        TotalDiffs = _hunkIndices.Count;
        CurrentDiffIndex = TotalDiffs > 0 ? 0 : -1;

        Lines.Clear();
        foreach (var line in result.Lines)
            Lines.Add(new DiffLineViewModel(line));

        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(HasDiffs));

        if (CurrentDiffIndex >= 0)
            NavigateToCurrentHunk();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PrevDiff()
    {
        if (CurrentDiffIndex > 0)
        {
            CurrentDiffIndex--;
            NavigateToCurrentHunk();
        }
        OnPropertyChanged(nameof(DiffCounterText));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoPrev() => CurrentDiffIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDiff()
    {
        if (CurrentDiffIndex < TotalDiffs - 1)
        {
            CurrentDiffIndex++;
            NavigateToCurrentHunk();
        }
        OnPropertyChanged(nameof(DiffCounterText));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoNext() => CurrentDiffIndex >= 0 && CurrentDiffIndex < TotalDiffs - 1;

    private void NavigateToCurrentHunk()
    {
        if (CurrentDiffIndex >= 0 && CurrentDiffIndex < _hunkIndices.Count)
        {
            int lineIndex = _hunkIndices[CurrentDiffIndex];
            // Each line row is 24px tall
            ScrollOffset = lineIndex * 24.0;
        }
    }

    partial void OnCurrentDiffIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DiffCounterText));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalDiffsChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffs));
        OnPropertyChanged(nameof(DiffCounterText));
    }
}
