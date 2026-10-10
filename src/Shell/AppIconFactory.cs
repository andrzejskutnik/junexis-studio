using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Junexis.Studio.Shell;

/// <summary>Renders the Junexis nexus mark into a bitmap so every window can share it as its icon/taskbar glyph.</summary>
internal static class AppIconFactory
{
    private static WindowIcon? _icon;

    /// <summary>Lazily rendered, shared window icon (accent badge + <c>StudioIconLogo</c> mark).</summary>
    public static WindowIcon Icon => _icon ??= Render();

    private static WindowIcon Render()
    {
        const int size = 64;
        var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#3794C7"), 0),
                    new GradientStop(Color.Parse("#007ACC"), 1),
                },
            };
            context.DrawRectangle(background, null, new Rect(0, 0, size, size));

            if (Application.Current?.Resources.TryGetResource("StudioIconLogo", null, out var resource) == true
                && resource is StreamGeometry geometry)
            {
                var scale = (size - 16) / 24.0;
                using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(8, 8)))
                {
                    context.DrawGeometry(Brushes.White, null, geometry);
                }
            }
        }

        return new WindowIcon(target);
    }
}
