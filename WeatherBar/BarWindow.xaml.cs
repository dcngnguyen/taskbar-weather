using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WeatherBar;

/// <summary>
/// The small weather bar on the taskbar. It sits just left of Music Bar when that is running,
/// otherwise just left of the tray icons.
/// </summary>
public partial class BarWindow : Window
{
    private const double BarHeight = 40, Gap = 8, PopupGap = 8, SidePadding = 12;
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    private readonly IntPtr _hwnd;
    private readonly Settings _settings;
    private readonly ForecastPopup _popup;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _refresh = new() { Interval = RefreshEvery };
    private readonly DispatcherTimer _openDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _closeDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Native.WinEventProc _foregroundChanged;
    private IntPtr _foregroundHook;
    private double _scale = 1;

    private Forecast? _forecast;
    private DateTime _fetchedAt;
    private string? _error;
    private int _fetchVersion;
    private DateTime _fetchStartedAt;

    public BarWindow()
    {
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.MakeToolWindow(_hwnd, noActivate: true);

        _settings = Settings.Load();
        // The small symbol moves too, at 30 frames a second to go easy on the computer,
        // and only while the bar is showing (not during full-screen games).
        _symbol = new SymbolMotion(IconText, distance: 0.5, frameRate: 30);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _symbol.Start(_motion);
            else _symbol.Stop();
        };
        _popup = new ForecastPopup();
        _popup.PlaceChosen += OnPlaceChosen;
        // Signing out or shutting down Windows must be able to close everything.
        Application.Current.SessionEnding += (_, _) => _quitting = _popup.AllowClose = true;

        // Hover over the bar to open the forecast; it closes once the mouse has left both windows.
        Bar.MouseEnter += (_, _) =>
        {
            _closeDelay.Stop();
            if (!_popup.IsOpen) _openDelay.Start();
        };
        Bar.MouseLeave += (_, _) =>
        {
            _openDelay.Stop();
            if (_popup.IsOpen) _closeDelay.Start();
        };
        _popup.MouseEnter += (_, _) => _closeDelay.Stop();
        _popup.MouseLeave += (_, _) => _closeDelay.Start();
        _openDelay.Tick += (_, _) =>
        {
            _openDelay.Stop();
            if (Bar.IsMouseOver && IsVisible) OpenPopup();
        };
        _closeDelay.Tick += (_, _) =>
        {
            _closeDelay.Stop();
            // While you're typing a city, it stays until you click elsewhere.
            if (!Bar.IsMouseOver && !_popup.IsMouseOver && !_popup.KeepOpen) _popup.HideAnimated();
        };

        _refresh.Tick += async (_, _) => await RefreshAsync();
        _foregroundChanged = (_, _, _, _, _, _, _) => UpdatePlacement();
        _timer.Tick += (_, _) => UpdatePlacement();
    }

    public async void Start()
    {
        ShowWeather();
        UpdatePlacement();
        _timer.Start();
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundChanged, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        await RefreshAsync();
    }

    // ---------- Weather ----------

    /// <summary>Downloads the forecast. On failure the last forecast stays up and it retries soon.</summary>
    private async Task RefreshAsync()
    {
        if (_settings is not { Latitude: { } lat, Longitude: { } lon }) return;
        int version = ++_fetchVersion;
        _fetchStartedAt = DateTime.Now;
        try
        {
            var forecast = await WeatherClient.GetForecastAsync(lat, lon, CancellationToken.None);
            if (version != _fetchVersion) return; // a newer request (e.g. another city) took over
            _forecast = forecast;
            _fetchedAt = DateTime.Now;
            _error = null;
            RestartRefreshTimer(RefreshEvery);
        }
        catch (Exception ex)
        {
            if (version != _fetchVersion) return;
            Log.Write("Could not get the forecast: " + ex.Message);
            // Hours-old weather is misleading (e.g. after a night's sleep offline), so stop showing it.
            if (_forecast is not null && DateTime.Now - _fetchedAt > TimeSpan.FromHours(3)) _forecast = null;
            _error = _forecast is null
                ? "Couldn't get the weather. Trying again in 2 minutes."
                : $"Couldn't update. Showing weather from {_fetchedAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}.";
            RestartRefreshTimer(RetryAfter);
        }
        ShowWeather();
    }

    private void RestartRefreshTimer(TimeSpan interval)
    {
        _refresh.Stop();
        _refresh.Interval = interval;
        _refresh.Start();
    }

    private void OnPlaceChosen(Place place)
    {
        _settings.CityName = place.Name;
        _settings.Latitude = place.Latitude;
        _settings.Longitude = place.Longitude;
        try { _settings.Save(); }
        catch (Exception ex) { Log.Write("Could not save the city: " + ex.Message); }

        _forecast = null; // don't show the old city's weather under the new name
        _error = null;
        ShowWeather();
        _ = RefreshAsync();
    }

    /// <summary>Updates the bar and the forecast window with the latest data.</summary>
    private void ShowWeather()
    {
        var current = _forecast?.Current;
        WeatherLook? look = current is null ? null : WeatherIcons.For(current.Code, current.IsDay != 0);
        string temp = !_settings.HasCity ? "Set city" : current is null ? "--°" : WeatherIcons.Temp(current.Temperature);

        bool changed = temp != TempText.Text || look?.Glyph != (IconText.Visibility == Visibility.Visible ? IconText.Text : null);
        // Real weather replacing other real weather gets a little swap animation; anything else just updates.
        if (changed && IsVisible && current is not null && IconText.Visibility == Visibility.Visible) SwapBar(look, temp);
        else if (!_swapping) SetBar(look, temp);
        else (_pendingLook, _pendingTemp) = (look, temp);

        _popup.ShowWeather(_settings.HasCity ? _settings.CityName ?? "Your city" : null, _forecast, _error, _fetchedAt);
    }

    private void SetBar(WeatherLook? look, string temp)
    {
        if (look is not null)
        {
            IconText.Text = look.Glyph;
            IconText.Foreground = look.Color;
        }
        IconText.Visibility = look is null ? Visibility.Collapsed : Visibility.Visible;
        TempText.Text = temp;
        _motion = look?.Motion;
        if (IsVisible) _symbol.Start(_motion);
        UpdatePlacement(); // the bar's width follows its text
    }

    private bool _swapping;
    private WeatherLook? _pendingLook;
    private string _pendingTemp = "";
    private readonly TranslateTransform _contentShift = new();
    private readonly SymbolMotion _symbol;
    private IconMotion? _motion;

    /// <summary>The old reading slides up and fades out, then the new one rises in from below.</summary>
    private void SwapBar(WeatherLook? look, string temp)
    {
        (_pendingLook, _pendingTemp) = (look, temp);
        if (_swapping) return; // the running swap will show the newest values
        _swapping = true;
        BarContent.RenderTransform = _contentShift;

        var outTime = TimeSpan.FromMilliseconds(140);
        var inTime = TimeSpan.FromMilliseconds(240);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation(0, outTime) { EasingFunction = easeIn };
        fadeOut.Completed += (_, _) =>
        {
            _swapping = false;
            SetBar(_pendingLook, _pendingTemp);
            BarContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, inTime) { EasingFunction = easeOut });
            _contentShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, inTime) { EasingFunction = easeOut });
        };
        BarContent.BeginAnimation(OpacityProperty, fadeOut);
        _contentShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, outTime) { EasingFunction = easeIn });
    }

    // ---------- Placement ----------

    /// <summary>Keeps the bar on the taskbar, above it in the stack, and hidden during full-screen apps.</summary>
    private void UpdatePlacement()
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out var tb) ||
            Native.IsFullscreenAppActive() || !Native.IsTaskbarShown(taskbar, tb))
        {
            if (IsVisible && !_barHiding) FadeBar(show: false);
            if (_popup.IsVisible) _popup.Hide();
            return;
        }
        if (_barHiding) FadeBar(show: true); // came back before the fade-out finished

        uint dpi = Native.GetDpiForWindow(taskbar);
        double scale = dpi > 0 ? dpi / 96.0 : 1;
        int Px(double dip) => (int)Math.Round(dip * scale);
        _scale = scale;

        var tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        int right = tray != IntPtr.Zero && Native.GetWindowRect(tray, out var tr) && tr.Width > 0
            ? tr.Left - Px(Gap)
            : tb.Right - Px(360);

        // Sit to the left of Music Bar if it's showing on this taskbar.
        var music = Native.FindWindow(null, "Music Bar");
        if (music != IntPtr.Zero && Native.IsWindowVisible(music) && Native.GetWindowRect(music, out var mb) &&
            mb.Width > 0 && mb.Top < tb.Bottom && mb.Bottom > tb.Top && mb.Left <= right)
        {
            right = mb.Left - Px(Gap);
        }

        BarContent.InvalidateMeasure(); // otherwise WPF may reuse the size from before the text changed
        BarContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int width = Px(BarContent.DesiredSize.Width + SidePadding * 2 + 2);
        int height = Px(BarHeight);
        int x = right - width;
        int y = tb.Top + (tb.Height - height) / 2;

        bool firstShow = !IsVisible;
        if (firstShow)
        {
            Left = x / scale;
            Top = y / scale;
            Opacity = 0;
            Show();
            FadeBar(show: true);
        }

        // Clicking the taskbar lifts it over us, so lift the bar back on top when that happens.
        uint flags = Native.SWP_NOACTIVATE;
        if (!firstShow && !Native.IsAbove(taskbar, _hwnd)) flags |= Native.SWP_NOZORDER;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, width, height, flags);
        // The bar moves when Music Bar starts, quits or is resized: keep the forecast above it.
        if (_popup.IsOpen) _popup.PlaceAbove((x + width / 2.0) / scale, y / scale - PopupGap);
    }

    private bool _barHiding;
    private int _barFadeVersion;

    /// <summary>Fades the whole bar in, or out and then hides it (e.g. when a full-screen game starts).</summary>
    private void FadeBar(bool show)
    {
        int version = ++_barFadeVersion;
        _barHiding = !show;
        var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 220 : 150));
        if (!show)
        {
            fade.Completed += (_, _) =>
            {
                if (version != _barFadeVersion) return; // shown again meanwhile
                _barHiding = false;
                Hide();
            };
        }
        BeginAnimation(OpacityProperty, fade);
    }

    private void OpenPopup()
    {
        if (!Native.GetWindowRect(_hwnd, out var bar)) return;
        _popup.ShowAbove((bar.Left + bar.Right) / 2.0 / _scale, bar.Top / _scale - PopupGap);
        // Opening it is a good moment to fetch fresh weather if what we have is getting old.
        // (Not while a download is still under way: starting another would throw that one away.)
        bool stale = _forecast is null || DateTime.Now - _fetchedAt > TimeSpan.FromMinutes(10);
        if (stale && DateTime.Now - _fetchStartedAt > TimeSpan.FromSeconds(20)) _ = RefreshAsync();
    }

    // ---------- Menu and closing ----------

    /// <summary>
    /// The bar never takes focus, but a menu only notices clicks elsewhere when its window has focus.
    /// So take focus while the menu is open; clicking away then closes it.
    /// </summary>
    private void Menu_Opened(object sender, RoutedEventArgs e) => Native.SetForegroundWindow(_hwnd);

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _refresh.Stop();
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        _popup.AllowClose = true;
        _quitting = true;
        Application.Current.Shutdown();
    }

    private bool _quitting;

    /// <summary>Alt+F4 must not destroy the bar while the app keeps running. Only "Quit" closes it.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_quitting) e.Cancel = true;
        base.OnClosing(e);
    }
}
