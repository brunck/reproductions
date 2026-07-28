[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # adb serial. Defaults to the only connected device.
    [string] $DeviceId = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    throw 'adb not found in PATH. Install the Android SDK platform-tools.'
}

$connected = @()
foreach ($line in (& adb devices | Select-Object -Skip 1)) {
    $parts = -split $line.Trim()
    if ($parts.Length -ge 2 -and $parts[1] -eq 'device') { $connected += $parts[0] }
}

if ([string]::IsNullOrWhiteSpace($DeviceId)) {
    if ($connected.Count -eq 0) { throw 'No connected Android device. Connect one or start an emulator.' }
    if ($connected.Count -gt 1) { Write-Warning "Multiple devices; using $($connected[0]). Pass -DeviceId to choose." }
    $DeviceId = $connected[0]
} elseif ($connected -notcontains $DeviceId) {
    throw "DeviceId '$DeviceId' not connected. Found: $($connected -join ', ')"
}

$env:ANDROID_SERIAL = $DeviceId
Write-Host "Deploying to $DeviceId ($Configuration)" -ForegroundColor Cyan

$project = Join-Path $PSScriptRoot '..\BindablePropertyConcurrencyRepro.csproj' | Resolve-Path
& dotnet build $project -t:Run -f net10.0-android -c $Configuration -v minimal
