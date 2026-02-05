[CmdletBinding()]
param(
    [string] $ProjectPath = "",
    [string] $Configuration = "Debug",
    [switch] $Profiling
)

$ErrorActionPreference = 'Stop'

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

Write-Host "Building MAUI Android... (Project: $ProjectPath, Configuration: $Configuration)" -ForegroundColor Cyan
# Prepare extra MSBuild properties when profiling is requested
$extraProps = @()
if ($Profiling) {
    Write-Host "Profiling build: enabling Android profiler properties via EnableAndroidProfiling MSBuild switch." -ForegroundColor Yellow
    $extraProps += "-p:EnableAndroidProfiling=true"
}

& dotnet build $ProjectPath -f net10.0-android -c $Configuration -v minimal $extraProps
