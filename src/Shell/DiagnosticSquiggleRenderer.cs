using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

internal sealed class DiagnosticSquiggleRenderer : IBackgroundRenderer, IDisposable
{
    private static readonly Pen ErrorPen = new(new SolidColorBrush(Color.Parse("#F14C4C")), 1.4);
    private static readonly Pen WarningPen = new(new SolidColorBrush(Color.Parse("#CCA700")), 1.3);
    private static readonly Pen InfoPen = new(new SolidColorBrush(Color.Parse("#3794FF")), 1.2);
    private readonly TextView _textView;
    private readonly IEditorDiagnosticService _diagnostics;
    private readonly Func<string?> _path;

    public DiagnosticSquiggleRenderer(
        TextView textView,
        IEditorDiagnosticService diagnostics,
        Func<string?> path)
    {
        _textView = textView;
        _diagnostics = diagnostics;
        _path = path;
        _diagnostics.DiagnosticsChanged += Invalidate;
    }

    public KnownLayer Layer => KnownLayer.Text;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var path = _path();
        var document = textView.Document;
        if (string.IsNullOrWhiteSpace(path) || document is null || !textView.VisualLinesValid)
            return;

        foreach (var diagnostic in _diagnostics.GetForPath(path))
        {
            if (diagnostic.Line is not { } line || line < 1 || line > document.LineCount)
                continue;
            var documentLine = document.GetLineByNumber(line);
            if (documentLine.Length == 0)
                continue;
            var start = documentLine.Offset
                        + Math.Clamp((diagnostic.Column ?? 1) - 1, 0, documentLine.Length - 1);
            var length = FindUnderlineLength(document, documentLine, start);
            var segment = new TextSegment { StartOffset = start, Length = length };
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawSquiggle(drawingContext, rectangle, PenFor(diagnostic.Severity));
        }
    }

    public void Dispose()
    {
        _diagnostics.DiagnosticsChanged -= Invalidate;
        _textView.BackgroundRenderers.Remove(this);
    }

    private void Invalidate()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _textView.InvalidateVisual();
        else
            Dispatcher.UIThread.Post(_textView.InvalidateVisual, DispatcherPriority.Render);
    }

    private static int FindUnderlineLength(TextDocument document, DocumentLine line, int start)
    {
        if (line.Length == 0 || start >= line.EndOffset)
            return 1;
        var end = start;
        while (end < line.EndOffset && IsWordCharacter(document.GetCharAt(end)))
            end++;
        if (end == start)
            end = Math.Min(line.EndOffset, start + 1);
        return Math.Max(1, end - start);
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';

    private static Pen PenFor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => ErrorPen,
        DiagnosticSeverity.Warning => WarningPen,
        _ => InfoPen,
    };

    private static void DrawSquiggle(DrawingContext context, Rect rectangle, Pen pen)
    {
        const double step = 2.5;
        const double amplitude = 1.5;
        var y = rectangle.Bottom - 1;
        var x = rectangle.Left;
        var up = true;
        while (x < rectangle.Right)
        {
            var next = Math.Min(rectangle.Right, x + step);
            context.DrawLine(
                pen,
                new Point(x, y + (up ? 0 : amplitude)),
                new Point(next, y + (up ? amplitude : 0)));
            x = next;
            up = !up;
        }
    }
}
