using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WeatherBar;

/// <summary>
/// Gentle, endless movement for a weather symbol: the sun turns slowly, the moon rocks,
/// clouds drift sideways, and rain, snow and storms bob up and down.
/// </summary>
internal sealed class SymbolMotion
{
    private readonly TextBlock _symbol;
    private readonly double _distance;
    private readonly int? _frameRate;
    private readonly RotateTransform _turn = new();
    private readonly TranslateTransform _move = new();

    /// <param name="distance">How far it drifts and bobs, as a share of the big symbol's movement.</param>
    /// <param name="frameRate">Frames per second, or null for the screen's normal rate.</param>
    public SymbolMotion(TextBlock symbol, double distance, int? frameRate)
    {
        _symbol = symbol;
        _distance = distance;
        _frameRate = frameRate;
        symbol.RenderTransform = new TransformGroup { Children = { _turn, _move } };
    }

    /// <summary>Starts the movement that suits the symbol now showing (or just stops, for null).</summary>
    public void Start(IconMotion? motion)
    {
        Stop();
        if (motion is null || string.IsNullOrEmpty(_symbol.Text)) return;
        _symbol.RenderTransformOrigin = InkCenter();
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimation Swing(double from, double to, double seconds) => Tune(
            new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, EasingFunction = sine });

        switch (motion)
        {
            case IconMotion.Spin:
                _turn.BeginAnimation(RotateTransform.AngleProperty, Tune(new DoubleAnimation(0, 360, TimeSpan.FromSeconds(24))));
                break;
            case IconMotion.Rock:
                _turn.BeginAnimation(RotateTransform.AngleProperty, Swing(-8, 8, 2.5));
                break;
            case IconMotion.Drift:
                _move.BeginAnimation(TranslateTransform.XProperty, Swing(-3 * _distance, 3 * _distance, 3));
                break;
            case IconMotion.Bob:
                _move.BeginAnimation(TranslateTransform.YProperty, Swing(-2 * _distance, 2 * _distance, 1.2));
                break;
        }
    }

    public void Stop()
    {
        _turn.BeginAnimation(RotateTransform.AngleProperty, null);
        _move.BeginAnimation(TranslateTransform.XProperty, null);
        _move.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private DoubleAnimation Tune(DoubleAnimation animation)
    {
        animation.RepeatBehavior = RepeatBehavior.Forever;
        if (_frameRate is { } fps) Timeline.SetDesiredFrameRate(animation, fps);
        return animation;
    }

    /// <summary>
    /// Where the symbol's drawing actually sits within its text box (as fractions). Symbols aren't
    /// centered in the box, so turning around the box center would make the sun wobble in a circle.
    /// </summary>
    private Point InkCenter()
    {
        var text = new FormattedText(_symbol.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_symbol.FontFamily, _symbol.FontStyle, _symbol.FontWeight, _symbol.FontStretch),
            _symbol.FontSize, Brushes.White, VisualTreeHelper.GetDpi(_symbol).PixelsPerDip);
        var ink = text.BuildGeometry(new Point(0, 0)).Bounds;
        if (ink.IsEmpty || text.Width <= 0 || text.Height <= 0) return new Point(0.5, 0.5);
        return new Point((ink.Left + ink.Right) / 2 / text.Width, (ink.Top + ink.Bottom) / 2 / text.Height);
    }
}
