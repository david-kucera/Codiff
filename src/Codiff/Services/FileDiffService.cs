using Codiff.Models;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Codiff.Services;

public class FileDiffService
{
    private readonly ISideBySideDiffBuilder _diffBuilder = new SideBySideDiffBuilder(new Differ());

    public FileDiffResult DiffFiles(string leftPath, string rightPath)
    {
        var leftText = File.ReadAllText(leftPath);
        var rightText = File.ReadAllText(rightPath);
        return DiffTexts(leftText, rightText, leftPath, rightPath);
    }

    public FileDiffResult DiffTexts(string leftText, string rightText, string leftPath = "", string rightPath = "")
    {
        var model = _diffBuilder.BuildDiffModel(leftText, rightText);

        var lines = new List<DiffLine>();
        var hunkIndices = new List<int>();

        int count = Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        bool prevWasDiff = false;

        for (int i = 0; i < count; i++)
        {
            var oldPiece = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var newPiece = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;

            var diffLine = BuildDiffLine(oldPiece, newPiece);
            lines.Add(diffLine);

            bool isDiff = diffLine.LineType != LineType.Unchanged;
            if (isDiff && !prevWasDiff)
                hunkIndices.Add(i);

            prevWasDiff = isDiff;
        }

        return new FileDiffResult
        {
            LeftFilePath = leftPath,
            RightFilePath = rightPath,
            Lines = lines,
            DiffHunkIndices = hunkIndices
        };
    }

    private static DiffLine BuildDiffLine(DiffPiece? oldPiece, DiffPiece? newPiece)
    {
        if (oldPiece == null && newPiece == null)
            return new DiffLine { LineType = LineType.Filler };

        // Determine type based on the change types from both sides
        ChangeType oldType = oldPiece?.Type ?? ChangeType.Imaginary;
        ChangeType newType = newPiece?.Type ?? ChangeType.Imaginary;

        LineType lineType;
        if (oldType == ChangeType.Imaginary && newType == ChangeType.Imaginary)
            lineType = LineType.Filler;
        else if (oldType == ChangeType.Imaginary)
            lineType = LineType.Added;
        else if (newType == ChangeType.Imaginary)
            lineType = LineType.Deleted;
        else if (oldType == ChangeType.Unchanged && newType == ChangeType.Unchanged)
            lineType = LineType.Unchanged;
        else if (oldType == ChangeType.Modified || newType == ChangeType.Modified)
            lineType = LineType.Modified;
        else if (oldType == ChangeType.Deleted && newType == ChangeType.Inserted)
            lineType = LineType.Modified;
        else if (oldType == ChangeType.Inserted)
            lineType = LineType.Added;
        else if (oldType == ChangeType.Deleted)
            lineType = LineType.Deleted;
        else
            lineType = LineType.Unchanged;

        var leftSpans = BuildSpans(oldPiece);
        var rightSpans = BuildSpans(newPiece);

        return new DiffLine
        {
            LineType = lineType,
            LeftText = oldPiece?.Text ?? string.Empty,
            RightText = newPiece?.Text ?? string.Empty,
            LeftLineNumber = (oldType != ChangeType.Imaginary) ? oldPiece?.Position : null,
            RightLineNumber = (newType != ChangeType.Imaginary) ? newPiece?.Position : null,
            LeftSpans = leftSpans,
            RightSpans = rightSpans
        };
    }

    private static IReadOnlyList<DiffSpan> BuildSpans(DiffPiece? piece)
    {
        if (piece == null || piece.SubPieces.Count == 0)
        {
            return piece != null
                ? new[] { new DiffSpan { Text = piece.Text ?? string.Empty, IsChanged = false } }
                : Array.Empty<DiffSpan>();
        }

        var spans = new List<DiffSpan>();
        foreach (var sub in piece.SubPieces)
        {
            if (sub.Text == null) continue;
            spans.Add(new DiffSpan
            {
                Text = sub.Text,
                IsChanged = sub.Type != ChangeType.Unchanged
            });
        }
        return spans;
    }
}
