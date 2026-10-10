using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

internal sealed class ExecutionLineRenderer : IBackgroundRenderer, IDisposable
{
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#334B6E"), 0.62);
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#F5C542"));
    private readonly TextView _textView;
    private readonly IDebugExecutionLocationService _locations;
    private readonly Func<string?> _path;

    public ExecutionLineRenderer(
        TextView textView,
        IDebugExecutionLocationService locations,
        Func<string?> path)
    {
        _textView = textView;
        _locations = locations;
        _path = path;
        _locations.LocationChanged += Invalidate;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var location = _locations.Current;
        var path = _path();
        if (location is null || string.IsNullOrWhiteSpace(path)
            || !Path.GetFullPath(path).Equals(location.Path, StringComparison.OrdinalIgnoreCase)
            || !textView.VisualLinesValid)
            return;
        var visualLine = textView.VisualLines.FirstOrDefault(item =>
            item.FirstDocumentLine.LineNumber <= location.Line
            && item.LastDocumentLine.LineNumber >= location.Line);
        if (visualLine is null)
            return;
        var top = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop)
                  - textView.VerticalOffset;
        var bottom = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[^1], VisualYPosition.LineBottom)
                     - textView.VerticalOffset;
        drawingContext.DrawRectangle(LineBrush, null, new Rect(0, top, textView.Bounds.Width, bottom - top));
        drawingContext.DrawRectangle(AccentBrush, null, new Rect(0, top, 3, bottom - top));
    }

    public void Dispose()
    {
        _locations.LocationChanged -= Invalidate;
        _textView.BackgroundRenderers.Remove(this);
    }

    public void Invalidate()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _textView.InvalidateVisual();
        else
            Dispatcher.UIThread.Post(_textView.InvalidateVisual, DispatcherPriority.Render);
    }
}
