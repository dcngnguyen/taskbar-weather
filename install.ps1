# Builds Weather Bar, copies it to %LOCALAPPDATA%\Programs\WeatherBar, and starts it.
# Run again after any code change to update the installed copy.
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\WeatherBar'

Get-Process WeatherBar -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish "$PSScriptRoot\WeatherBar\WeatherBar.csproj" -c Release -r win-x64 --self-contained false -o $dest
if ($LASTEXITCODE -ne 0) { throw 'Build failed, nothing was installed.' }

Start-Process (Join-Path $dest 'WeatherBar.exe')
Write-Host "Weather Bar installed to $dest and started."
