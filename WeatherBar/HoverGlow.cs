using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WeatherBar;

/// <summary>
/// The hover effect all three taskbar bars share: while the mouse is over the bar, its background
/// and border brighten a little and its icon grows slightly. Both settle back when the mouse leaves.
/// </summary>
internal static class HoverGlow
{
    private static readonly Duration Fade = TimeSpan.FromMilliseconds(150);
    private static readonly Duration Grow = TimeSpan.FromMilliseconds(200);
    private const double IconScale = 1.12;

    public static void Attach(Border bar, UIElement icon)
    {
        // Own copies of the colors, since the shared ones from the app's resources can't be animated.
        var rest = ((SolidColorBrush)bar.Background).Color;
        var restEdge = ((SolidColorBrush)bar.BorderBrush).Color;
        var lit = Color.FromArgb(rest.A, Lighter(rest.R), Lighter(rest.G), Lighter(rest.B));
        var litEdge = Color.FromArgb((byte)Math.Min(255, restEdge.A * 2), restEdge.R, restEdge.G, restEdge.B);
        var background = new SolidColorBrush(rest);
        var edge = new SolidColorBrush(restEdge);
        bar.Background = background;
        bar.BorderBrush = edge;

        var scale = new ScaleTransform();
        icon.RenderTransform = scale;
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        void Show(bool hover)
        {
            background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hover ? lit : rest, Fade));
            edge.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hover ? litEdge : restEdge, Fade));
            var size = new DoubleAnimation(hover ? IconScale : 1, Grow) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
        }
        bar.MouseEnter += (_, _) => Show(true);
        bar.MouseLeave += (_, _) => Show(false);
    }

    private static byte Lighter(byte channel) => (byte)Math.Min(255, channel + 12);
}
