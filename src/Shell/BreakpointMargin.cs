using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

internal sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 18;
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));
    private static readonly IBrush VerifiedBrush = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush AdjustedBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush PartialBrush = new SolidColorBrush(Color.Parse("#A855F7"));
    private static readonly IBrush RejectedBrush = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly Pen PendingPen = new(PendingBrush, 1.5);
    private static readonly Pen CrossPen = new(Brushes.White, 1.4);
    private readonly IBreakpointGlyphService _glyphs;
    private readonly Func<string?> _path;
    private bool _subscribed;

    public BreakpointMargin(
        IBreakpointGlyphService glyphs,
        Func<string?> path)
    {
        _glyphs = glyphs;
        _path = path;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (TextView is not { VisualLinesValid: true } || !TryGetPath(out var path))
            return;
        var glyphs = _glyphs.GetGlyphs(path).ToDictionary(item => item.Line);
        foreach (var visualLine in TextView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine.LineNumber;
            if (!glyphs.TryGetValue(line, out var glyph))
                continue;
            var y = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextMiddle)
                    - TextView.VerticalOffset;
            DrawGlyph(context, new Point(MarginWidth / 2, y), glyph);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_subscribed)
            return;
        _glyphs.GlyphsChanged += OnGlyphsChanged;
        _subscribed = true;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed)
        {
            _glyphs.GlyphsChanged -= OnGlyphsChanged;
            _subscribed = false;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !TryGetPath(out var path) || TryGetLine(e) is not { } line)
            return;
        e.Handled = true;
        try
        {
            await _glyphs.ToggleAsync(path, line);
        }
        catch (Exception ex)
        {
            ToolTip.SetTip(this, $"Cannot update breakpoint: {ex.Message}");
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!TryGetPath(out var path) || TryGetLine(e) is not { } line)
        {
            ToolTip.SetTip(this, null);
            return;
        }
        var glyph = _glyphs.GetGlyphs(path).FirstOrDefault(item => item.Line == line);
        ToolTip.SetTip(this, glyph?.ToolTip ?? $"Click to add a breakpoint at line {line}.");
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

    private bool TryGetPath(out string path)
    {
        path = _path() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static void DrawGlyph(DrawingContext context, Point center, BreakpointGlyph glyph)
    {
        const double radius = 5.5;
        var state = glyph.State;
        if (state == BreakpointGlyphState.Pending)
        {
            if (glyph.IsLogpoint)
                DrawDiamond(context, center, null, PendingPen, radius);
            else
                context.DrawEllipse(null, PendingPen, center, radius, radius);
            return;
        }

        var brush = state switch
        {
            BreakpointGlyphState.Verified => VerifiedBrush,
            BreakpointGlyphState.Adjusted => AdjustedBrush,
            BreakpointGlyphState.PartiallyVerified => PartialBrush,
            BreakpointGlyphState.Rejected => RejectedBrush,
            _ => PendingBrush,
        };
        if (glyph.IsLogpoint)
            DrawDiamond(context, center, brush, null, radius);
        else
            context.DrawEllipse(brush, null, center, radius, radius);
        if (state == BreakpointGlyphState.Adjusted)
            context.DrawEllipse(Brushes.White, null, center, 1.7, 1.7);
        else if (state == BreakpointGlyphState.PartiallyVerified)
            context.DrawEllipse(null, new Pen(Brushes.White, 1.2), center, 3.2, 3.2);
        else if (state == BreakpointGlyphState.Rejected)
        {
            context.DrawLine(CrossPen, center + new Vector(-2.5, -2.5), center + new Vector(2.5, 2.5));
            context.DrawLine(CrossPen, center + new Vector(-2.5, 2.5), center + new Vector(2.5, -2.5));
        }
        else if (glyph.HasCondition)
            context.DrawEllipse(Brushes.White, null, center, 1.7, 1.7);
    }

    private static void DrawDiamond(DrawingContext context, Point center, IBrush? fill, Pen? pen, double radius)
    {
        var geometry = new StreamGeometry();
        using (var target = geometry.Open())
        {
            target.BeginFigure(center + new Vector(0, -radius), true);
            target.LineTo(center + new Vector(radius, 0));
            target.LineTo(center + new Vector(0, radius));
            target.LineTo(center + new Vector(-radius, 0));
            target.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private void OnGlyphsChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}
