[CmdletBinding()]
param(
    [string] $ProjectPath = "",
    [string] $Configuration = "Release",
    [string] $DeviceId = "",
    [switch] $Profiling
)

$ErrorActionPreference = 'Stop'

# Handle common misuse where the `-Profiling` switch was passed a value (e.g. `-Profiling true`),
# which PowerShell binds as a positional string into `$ProjectPath`.
if ($ProjectPath -match '^(true|false)$') {
    Write-Warning "Detected boolean literal in ProjectPath parameter (likely from passing '-Profiling true'). Ignoring and auto-locating project."
    $ProjectPath = ""
}

# If ProjectPath wasn't provided, try to locate the csproj relative to the script file
if (-not $PSBoundParameters.ContainsKey('ProjectPath') -or [string]::IsNullOrWhiteSpace($ProjectPath)) {
    $scriptParent = Join-Path $PSScriptRoot '..'

    $candidate1 = Join-Path $scriptParent 'MauiTabbedModalLeakRepro.csproj'
    $candidate2 = Join-Path $scriptParent 'MauiTabbedModalLeakRepro\MauiTabbedModalLeakRepro.csproj'

    if (Test-Path $candidate1) {
        $ProjectPath = $candidate1
    } elseif (Test-Path $candidate2) {
        $ProjectPath = $candidate2
    } else {
        # Search recursively under the parent directory as a last resort
        $found = Get-ChildItem -Path $scriptParent -Filter 'MauiTabbedModalLeakRepro.csproj' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $ProjectPath = $found.FullName
        } else {
            throw "Could not locate MauiTabbedModalLeakRepro.csproj. Provide -ProjectPath to the script."
        }
    }
}


Write-Host "Deploying & running MAUI Android... (Project: $ProjectPath, Configuration: $Configuration)" -ForegroundColor Cyan

# Ensure adb exists and a device/emulator is available before attempting deployment
$adb = 'adb'
if (-not (Get-Command $adb -ErrorAction SilentlyContinue)) {
    throw "adb not found in PATH. Ensure Android SDK platform-tools are installed and adb is available."
}

$adbOutput = & $adb devices
$deviceLines = $adbOutput | Select-Object -Skip 1 | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
$connected = @()
foreach ($line in $deviceLines) {
    # typical line: <serial>\tdevice or <serial> <status>
    $parts = -split $line
    if ($parts.Length -ge 2 -and $parts[1] -eq 'device') {
        $connected += $parts[0]
    }
}

if ([string]::IsNullOrWhiteSpace($DeviceId)) {
    if ($connected.Count -eq 0) {
        throw "No connected Android device/emulator detected. Start an emulator or connect a device, then re-run. See: https://developer.android.com/studio/run/emulator"
    } elseif ($connected.Count -gt 1) {
        Write-Warning "Multiple Android devices detected. Using first device: $($connected[0]). To specify a different device, pass -DeviceId <serial>."
        $DeviceId = $connected[0]
    } else {
        $DeviceId = $connected[0]
    }
} else {
    if ($connected -notcontains $DeviceId) {
        throw "Requested DeviceId '$DeviceId' not found among connected devices: $($connected -join ', ')."
    }
}

# Export ANDROID_SERIAL so adb/msbuild targets pick the device when deploying
$env:ANDROID_SERIAL = $DeviceId

Write-Host "Deploy target device: $DeviceId" -ForegroundColor Cyan

# Prepare extra MSBuild properties when profiling is requested
$extraProps = @()
if ($Profiling) {
    Write-Host "Profiling run: enabling Android profiler properties via EnableAndroidProfiling MSBuild switch." -ForegroundColor Yellow
    $extraProps += "-p:EnableAndroidProfiling=true"
}

& dotnet build $ProjectPath -t:Run -f net10.0-android -c $Configuration -v minimal $extraProps
