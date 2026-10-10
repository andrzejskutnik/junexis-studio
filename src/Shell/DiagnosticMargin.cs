using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

internal sealed class DiagnosticMargin : AbstractMargin
{
    private const double MarginWidth = 14;
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F14C4C"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#CCA700"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#3794FF"));
    private readonly IEditorDiagnosticService _diagnostics;
    private readonly Func<string?> _path;
    private bool _subscribed;

    public DiagnosticMargin(IEditorDiagnosticService diagnostics, Func<string?> path)
    {
        _diagnostics = diagnostics;
        _path = path;
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (TextView is not { VisualLinesValid: true } || _path() is not { } path)
            return;
        var byLine = _diagnostics.GetForPath(path)
            .Where(diagnostic => diagnostic.Line is not null)
            .GroupBy(diagnostic => diagnostic.Line!.Value)
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.Severity)!);
        foreach (var visualLine in TextView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine.LineNumber;
            if (!byLine.TryGetValue(line, out var diagnostic))
                continue;
            var y = visualLine.GetTextLineVisualYPosition(
                        visualLine.TextLines[0],
                        VisualYPosition.TextMiddle)
                    - TextView.VerticalOffset;
            DrawGlyph(context, new Point(MarginWidth / 2, y), diagnostic.Severity);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_subscribed)
            return;
        _diagnostics.DiagnosticsChanged += OnDiagnosticsChanged;
        _subscribed = true;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed)
        {
            _diagnostics.DiagnosticsChanged -= OnDiagnosticsChanged;
            _subscribed = false;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_path() is not { } path || TryGetLine(e) is not { } line)
        {
            ToolTip.SetTip(this, null);
            return;
        }
        var diagnostics = _diagnostics.GetAt(path, line, 1);
        ToolTip.SetTip(
            this,
            diagnostics.Count == 0
                ? null
                : string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
    }

    private int? TryGetLine(PointerEventArgs e)
    {
        if (TextView is null)
            return null;
        var position = e.GetPosition(TextView);
        var visualLine = TextView.GetVisualLineFromVisualTop(
            Math.Clamp(position.Y, 0, TextView.Bounds.Height) + TextView.VerticalOffset);
        return visualLine?.FirstDocumentLine.LineNumber;
    }

    private void OnDiagnosticsChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private static string FormatDiagnostic(Diagnostic diagnostic) =>
        $"{diagnostic.Severity}: {diagnostic.Message}";

    private static void DrawGlyph(DrawingContext context, Point center, DiagnosticSeverity severity)
    {
        var brush = severity switch
        {
            DiagnosticSeverity.Error => ErrorBrush,
            DiagnosticSeverity.Warning => WarningBrush,
            _ => InfoBrush,
        };
        if (severity == DiagnosticSeverity.Warning)
        {
            var geometry = new StreamGeometry();
            using var target = geometry.Open();
            target.BeginFigure(center + new Vector(0, -5), true);
            target.LineTo(center + new Vector(5, 4));
            target.LineTo(center + new Vector(-5, 4));
            target.EndFigure(true);
            context.DrawGeometry(brush, null, geometry);
            return;
        }
        context.DrawEllipse(brush, null, center, 4.3, 4.3);
    }
}
