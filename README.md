# Weather Bar

A small weather readout on the Windows taskbar. It shows the current temperature and a weather symbol, and sits just left of Music Bar (or left of the tray icons if Music Bar isn't running). Hover over it to see a forecast: today's details, the next 12 hours and the next 7 days.

Weather data comes from [Open-Meteo](https://open-meteo.com), which is free and needs no account. Temperatures are in °C and wind in m/s.

## Install or update

Needs the .NET 10 SDK. In PowerShell, from this folder:

```powershell
.\install.ps1
```

This builds the app, copies it to `%LOCALAPPDATA%\Programs\WeatherBar`, starts it, and makes it start when you sign in.

## Use

- First run: hover over the bar, type your city, press Enter and pick the right match.
- Change city: click the city name at the top of the forecast window.
- Quit: right-click the bar.

It refreshes every 15 minutes (every 2 minutes after a failed try). It hides during full-screen games and videos.

## Uninstall

1. Right-click the bar and choose **Quit Weather Bar**.
2. Delete `%LOCALAPPDATA%\Programs\WeatherBar` and `%APPDATA%\WeatherBar`.
3. Remove the `WeatherBar` value from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (or just disable it in Task Manager, Startup apps).
