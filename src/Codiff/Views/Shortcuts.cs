using Avalonia.Input;

namespace Codiff.Views;

/// <summary>
/// Keyboard shortcuts of the file comparison. On macOS the Command key replaces Ctrl, and copying
/// a difference uses Cmd+Option+arrow because Option+arrow moves the caret by words there.
/// </summary>
public static class Shortcuts
{
    private static readonly bool IsMac = OperatingSystem.IsMacOS();

    /// <summary>Ctrl on Windows/Linux, Command on macOS.</summary>
    public static readonly KeyModifiers CommandModifier = IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    public static readonly KeyGesture Save = new(Key.S, CommandModifier);
    public static readonly KeyGesture Recompare = new(Key.F5);
    public static readonly KeyGesture NextDifference = new(Key.F8);
    public static readonly KeyGesture NextDifferenceAlt = new(Key.Down, KeyModifiers.Alt);
    public static readonly KeyGesture PreviousDifference = new(Key.F8, KeyModifiers.Shift);
    public static readonly KeyGesture PreviousDifferenceAlt = new(Key.Up, KeyModifiers.Alt);
    public static readonly KeyGesture FirstDifference = new(Key.Home, KeyModifiers.Alt);
    public static readonly KeyGesture LastDifference = new(Key.End, KeyModifiers.Alt);
    public static readonly KeyGesture CopyToRight = IsMac ? new(Key.Right, KeyModifiers.Meta | KeyModifiers.Alt) : new(Key.Right, KeyModifiers.Alt);
    public static readonly KeyGesture CopyToLeft = IsMac ? new(Key.Left, KeyModifiers.Meta | KeyModifiers.Alt) : new(Key.Left, KeyModifiers.Alt);
    public static readonly KeyGesture Undo = new(Key.Z, CommandModifier);
    public static readonly KeyGesture Find = new(Key.F, CommandModifier);
    public static readonly KeyGesture FindNext = new(Key.F3);
    public static readonly KeyGesture FindPrevious = new(Key.F3, KeyModifiers.Shift);
    public static readonly KeyGesture GoToLine = new(Key.G, KeyModifiers.Control);

    // Tooltips mention the platform's gesture.
    public static string SaveTip => $"Save ({Format(Save)})";
    public static string RecompareTip => $"Recompare ({Format(Recompare)})";
    public static string NextDifferenceTip => $"Next Difference ({Format(NextDifference)}, {Format(NextDifferenceAlt)})";
    public static string PreviousDifferenceTip => $"Previous Difference ({Format(PreviousDifference)}, {Format(PreviousDifferenceAlt)})";
    public static string FirstDifferenceTip => $"First Difference ({Format(FirstDifference)})";
    public static string LastDifferenceTip => $"Last Difference ({Format(LastDifference)})";
    public static string CopyToRightTip => $"Copy Difference to Right ({Format(CopyToRight)})";
    public static string CopyToLeftTip => $"Copy Difference to Left ({Format(CopyToLeft)})";
    public static string UndoTip => $"Undo Copy ({Format(Undo)})";
    public static string FindTip => $"Find ({Format(Find)}; next {Format(FindNext)})";
    public static string GoToLineTip => $"Go to Line ({Format(GoToLine)})";

    public static string Format(KeyGesture gesture) => gesture.ToString("p", null);
}
