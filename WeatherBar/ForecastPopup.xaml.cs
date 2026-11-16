using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WeatherBar;

/// <summary>The window above the bar: today's details, the next hours and the next days, or the city picker.</summary>
public partial class ForecastPopup : Window
{
    private sealed record HourItem(string Time, WeatherLook Look, string Temp);
    private sealed record DayItem(string Day, WeatherLook Look, string Rain, string Min, string Max);

    /// <summary>Raised when you pick a city from the search results.</summary>
    public event Action<Place>? PlaceChosen;

    /// <summary>Set when the app is quitting, so the window may really close.</summary>
    public bool AllowClose { get; set; }

    /// <summary>True while you're typing a city, so moving the mouse away won't close it.</summary>
    public bool KeepOpen => IsActive && Keyboard.FocusedElement is TextBox;

    private bool _hasCity;
    private bool _choosingCity;
    private CancellationTokenSource? _searchCts;
    private IconMotion? _motion;
    private readonly RotateTransform _iconTurn = new();
    private readonly TranslateTransform _iconMove = new();

    public ForecastPopup()
    {
        InitializeComponent();
        NowIcon.RenderTransform = new TransformGroup { Children = { _iconTurn, _iconMove } };
        Native.MakeToolWindow(new WindowInteropHelper(this).EnsureHandle(), noActivate: false);
        Deactivated += (_, _) => HideAnimated();
        // Alt+F4 would destroy the window for good; just hide it unless the app is quitting.
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            HideAnimated();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HideAnimated();
        };
        // Each time it opens, start from the forecast with an empty search.
        IsVisibleChanged += (_, _) =>
        {
            // The symbol only moves while you can see it.
            if (IsVisible)
            {
                StartIconMotion();
                return;
            }
            StopIconMotion();
            _searchCts?.Cancel();
            CityBox.Clear();
            PlaceList.ItemsSource = null;
            SetupMessage.Visibility = Visibility.Collapsed;
            _choosingCity = false;
            UpdateView();
        };
    }

    // ---------- Showing the weather ----------

    /// <param name="city">The chosen city's name, or null if none is chosen yet.</param>
    public void ShowWeather(string? city, Forecast? forecast, string? error, DateTime fetchedAt)
    {
        _hasCity = city is not null;
        CityText.Text = city ?? "Choose your city";

        var current = forecast?.Current;
        bool hadRows = DayList.Items.Count > 0;
        if (current is null)
        {
            ClearForecast();
            _motion = null;
            StopIconMotion();
            MessageText.Text = error ?? "Loading...";
            MessageText.Visibility = _hasCity ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            MessageText.Visibility = Visibility.Collapsed;
            var look = WeatherIcons.For(current.Code, current.IsDay != 0);
            NowIcon.Text = look.Glyph;
            NowIcon.Foreground = look.Color;
            NowTemp.Text = WeatherIcons.Temp(current.Temperature);
            NowLabel.Text = look.Label;
            FeelsText.Text = "Feels like " + WeatherIcons.Temp(current.FeelsLike);
            WindText.Text = current.Wind is { } wind ? $"Wind {Math.Round(wind):0} m/s" : "";
            // Same rows as before? Keep the old ones, so a refresh doesn't cut short their fade-in.
            var hours = Hours(forecast!.Hourly);
            var days = Days(forecast.Daily);
            if (HourList.ItemsSource is not IEnumerable<HourItem> oldHours || !oldHours.SequenceEqual(hours)) HourList.ItemsSource = hours;
            if (DayList.ItemsSource is not IEnumerable<DayItem> oldDays || !oldDays.SequenceEqual(days)) DayList.ItemsSource = days;

            StatusText.Text = error ?? "Updated " + fetchedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            StatusText.Foreground = (Brush)FindResource(error is null ? "TextSecondary" : "Error");

            if (_motion != look.Motion)
            {
                _motion = look.Motion;
                if (IsVisible) StartIconMotion();
            }
            // The forecast just arrived while you're looking (e.g. a new city): bring the rows in.
            if (!hadRows && IsVisible) AnimateRowsSoon();
        }
        UpdateView();
    }

    private void ClearForecast()
    {
        NowIcon.Text = NowTemp.Text = NowLabel.Text = FeelsText.Text = WindText.Text = StatusText.Text = "";
        HourList.ItemsSource = null;
        DayList.ItemsSource = null;
    }

    /// <summary>"Now" and then every 2 hours, 6 in all.</summary>
    private static List<HourItem> Hours(HourlyWeather? h)
    {
        var items = new List<HourItem>();
        int count = h?.Time?.Length ?? 0;
        for (int i = 0; i < count && items.Count < 6; i += 2)
        {
            var time = i == 0 ? "Now" : h!.Time![i] is { Length: >= 16 } t ? t.Substring(11, 5) : "";
            items.Add(new HourItem(time,
                WeatherIcons.For(At(h!.Code, i), At(h.IsDay, i) != 0),
                WeatherIcons.Temp(At(h.Temperature, i))));
        }
        return items;
    }

    private static List<DayItem> Days(DailyWeather? d)
    {
        var items = new List<DayItem>();
        int count = d?.Time?.Length ?? 0;
        for (int i = 0; i < count; i++)
        {
            string day = i == 0 ? "Today"
                : DateTime.TryParseExact(d!.Time![i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("ddd", CultureInfo.InvariantCulture) : "";
            // Only mention rain when there's a real chance of it.
            string rain = At(d!.RainChance, i) is >= 20 and var chance ? $"{chance}%" : "";
            items.Add(new DayItem(day, WeatherIcons.For(At(d.Code, i), isDay: true), rain,
                WeatherIcons.Temp(At(d.Min, i)), WeatherIcons.Temp(At(d.Max, i))));
        }
        return items;
    }

    /// <summary>The value at <paramref name="i"/>, or null if the list is missing or too short.</summary>
    private static T? At<T>(T?[]? list, int i) where T : struct => list is not null && i < list.Length ? list[i] : null;

    private void UpdateView()
    {
        bool setup = _choosingCity || !_hasCity;
        SetupView.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        ForecastView.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _hasCity ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Choosing a city ----------

    private void City_Click(object sender, RoutedEventArgs e)
    {
        _choosingCity = !_choosingCity || !_hasCity;
        UpdateView();
        if (SetupView.IsVisible) FocusCityBox();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _choosingCity = false;
        UpdateView();
    }

    /// <summary>The window never takes focus by itself, so take it before typing.</summary>
    private void FocusCityBox()
    {
        Activate();
        CityBox.Focus();
    }

    private async void CityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var query = CityBox.Text.Trim();
        if (query.Length < 2)
        {
            ShowSetupMessage("Type at least 2 letters.");
            return;
        }

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        PlaceList.ItemsSource = null;
        ShowSetupMessage("Searching...");
        try
        {
            var places = await WeatherClient.SearchAsync(query, cts.Token);
            if (cts != _searchCts || cts.IsCancellationRequested) return; // replaced, or the window closed
            PlaceList.ItemsSource = places;
            ShowSetupMessage(places.Count == 0 ? "No places found. Check the spelling." : null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Replaced by a newer search, or the window closed.
        }
        catch (Exception ex)
        {
            Log.Write("City search failed: " + ex.Message);
            if (cts == _searchCts) ShowSetupMessage("Couldn't search. Check your internet connection.");
        }
    }

    private void ShowSetupMessage(string? text)
    {
        SetupMessage.Text = text ?? "";
        SetupMessage.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Place place) return;
        _choosingCity = false;
        CityBox.Clear();
        PlaceList.ItemsSource = null;
        PlaceChosen?.Invoke(place);
    }

    // ---------- Animations ----------

    /// <summary>
    /// Fades the hours in left to right, then the days top to bottom. Waits until the rows
    /// have been laid out; until then the lists stay invisible so nothing flashes.
    /// </summary>
    private void AnimateRowsSoon()
    {
        HourList.Opacity = DayList.Opacity = 0;
        Dispatcher.InvokeAsync(() =>
        {
            HourList.Opacity = DayList.Opacity = 1;
            int n = 0;
            foreach (var list in new[] { HourList, DayList })
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (list.ItemContainerGenerator.ContainerFromIndex(i) is UIElement row)
                        FadeInRow(row, TimeSpan.FromMilliseconds(60 + 30 * n++));
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The row waits, hidden, until its turn, then fades in while rising 6px.</summary>
    private static void FadeInRow(UIElement row, TimeSpan delay)
    {
        if (row.RenderTransform is not TranslateTransform shift) row.RenderTransform = shift = new TranslateTransform();
        row.BeginAnimation(OpacityProperty, WaitThenGlide(0, 1, delay));
        shift.BeginAnimation(TranslateTransform.YProperty, WaitThenGlide(6, 0, delay));
    }

    /// <summary>Holds <paramref name="from"/> for <paramref name="delay"/>, then eases to <paramref name="to"/>.</summary>
    private static DoubleAnimationUsingKeyFrames WaitThenGlide(double from, double to, TimeSpan delay)
    {
        var end = delay + TimeSpan.FromMilliseconds(220);
        // FillBehavior.Stop: afterwards the row is back on its normal values (visible, in place).
        var animation = new DoubleAnimationUsingKeyFrames { Duration = end, FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(end), new CubicEase { EasingMode = EasingMode.EaseOut }));
        return animation;
    }

    /// <summary>
    /// Gentle, endless movement for the big symbol: the sun turns slowly, the moon rocks,
    /// clouds drift sideways, and rain, snow and storms bob up and down.
    /// </summary>
    private void StartIconMotion()
    {
        StopIconMotion();
        if (_motion is not { } motion) return;
        NowIcon.RenderTransformOrigin = InkCenter();
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimation Swing(double from, double to, double seconds) =>
            new(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine };

        switch (motion)
        {
            case IconMotion.Spin:
                _iconTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(24)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case IconMotion.Rock:
                _iconTurn.BeginAnimation(RotateTransform.AngleProperty, Swing(-8, 8, 2.5));
                break;
            case IconMotion.Drift:
                _iconMove.BeginAnimation(TranslateTransform.XProperty, Swing(-3, 3, 3));
                break;
            case IconMotion.Bob:
                _iconMove.BeginAnimation(TranslateTransform.YProperty, Swing(-2, 2, 1.2));
                break;
        }
    }

    /// <summary>
    /// Where the symbol's drawing actually sits within its text box (as fractions). Symbols aren't
    /// centered in the box, so turning around the box center would make the sun wobble in a circle.
    /// </summary>
    private Point InkCenter()
    {
        var text = new FormattedText(NowIcon.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(NowIcon.FontFamily, NowIcon.FontStyle, NowIcon.FontWeight, NowIcon.FontStretch),
            NowIcon.FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var ink = text.BuildGeometry(new Point(0, 0)).Bounds;
        if (ink.IsEmpty || text.Width <= 0 || text.Height <= 0) return new Point(0.5, 0.5);
        return new Point((ink.Left + ink.Right) / 2 / text.Width, (ink.Top + ink.Bottom) / 2 / text.Height);
    }

    private void StopIconMotion()
    {
        _iconTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        _iconMove.BeginAnimation(TranslateTransform.XProperty, null);
        _iconMove.BeginAnimation(TranslateTransform.YProperty, null);
    }

    // ---------- Opening and closing ----------

    private bool _closing;
    private int _animationVersion;

    /// <summary>Shown and not on its way out.</summary>
    public bool IsOpen => IsVisible && !_closing;

    /// <summary>Opens centered on <paramref name="centerX"/> with its bottom edge at <paramref name="bottom"/> (screen DIPs).</summary>
    public void ShowAbove(double centerX, double bottom)
    {
        bool fresh = !IsVisible;
        _closing = false;
        _animationVersion++;
        PlaceAbove(centerX, bottom);
        if (fresh)
        {
            AnimateRowsSoon();
            Show(); // not activated, so hovering never takes keyboard focus from what you're doing
        }
        // Fade in while sliding up. If it was mid-close, it turns around from where it is.
        Animate(to: 1, shift: 0, fresh ? 0 : null, fresh ? 12 : null, EasingMode.EaseOut, null);
    }

    /// <summary>Centers the popup on <paramref name="centerX"/> with its bottom edge at <paramref name="bottom"/> (screen DIPs).</summary>
    public void PlaceAbove(double centerX, double bottom)
    {
        Left = centerX - Width / 2;
        Top = bottom - Height;
    }

    /// <summary>Fades out while sliding down, then hides: the opening animation in reverse.</summary>
    public void HideAnimated()
    {
        if (!IsOpen) return;
        _closing = true;
        int version = ++_animationVersion;
        Animate(to: 0, shift: 12, null, null, EasingMode.EaseIn, () =>
        {
            // Only hide if it wasn't reopened while fading out.
            if (version != _animationVersion) return;
            _closing = false;
            Hide();
        });
    }

    private void Animate(double to, double shift, double? fromOpacity, double? fromShift, EasingMode mode, Action? done)
    {
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = mode };
        var fade = new DoubleAnimation { From = fromOpacity, To = to, Duration = duration, EasingFunction = ease };
        if (done is not null) fade.Completed += (_, _) => done();
        Root.BeginAnimation(OpacityProperty, fade);
        RootShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation { From = fromShift, To = shift, Duration = duration, EasingFunction = ease });
    }
}
