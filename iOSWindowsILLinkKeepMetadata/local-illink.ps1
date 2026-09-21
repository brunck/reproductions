<#
Runs ILLink directly on Windows (no Mac needed) against the compiled repro assembly, once without and once with
--keep-metadata all, then prints the MQTTnet constructor parameter names from each output.
Prerequisite: net11/obj/Debug/net11.0-ios/ios-arm64/TrimTest11.dll from a paired build (the build only has to get past
CoreCompile; the later native steps may fail).
#>
param(
    [string]$RuntimeVersion = "11.0.0-preview.7.26381.103",
    [string]$IosPackVersion = "26.5.11997-net11-p7",
    [string]$MqttNetVersion = "5.2.0.1603"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$nuget = Join-Path $env:USERPROFILE ".nuget\packages"
$illink = Join-Path $nuget "microsoft.net.illink.tasks\$RuntimeVersion\tools\net\illink.dll"
$runtimeLib = Join-Path $nuget "microsoft.netcore.app.runtime.ios-arm64\$RuntimeVersion\runtimes\ios-arm64\lib\net11.0"
$iosDll = "C:\Program Files\dotnet\packs\Microsoft.iOS.Runtime.ios.net11.0_26.5\$IosPackVersion\runtimes\ios\lib\net11.0\Microsoft.iOS.dll"
$mqtt = Join-Path $nuget "mqttnet\$MqttNetVersion\lib\net10.0\MQTTnet.dll"
$app = Join-Path $root "net11\obj\Debug\net11.0-ios\ios-arm64\TrimTest11.dll"
foreach ($p in $illink, $runtimeLib, $iosDll, $mqtt, $app) { if (-not (Test-Path $p)) { throw "Missing: $p" } }

foreach ($mode in "without", "with") {
    $out = Join-Path $root "local-illink-$mode"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory $out | Out-Null
    $args = @("-a", "TrimTest11", "all",
              "-reference", $app, "-reference", $mqtt, "-reference", $iosDll,
              "-d", $runtimeLib, "--trim-mode", "link", "-b", "--skip-unresolved", "true",
              "--feature", "System.Diagnostics.Debugger.IsSupported", "true", "--nowarn", "IL2026;IL2121",
              "-out", $out)
    if ($mode -eq "with") { $args += @("--keep-metadata", "all") }
    Write-Host "===== illink $mode --keep-metadata all"
    & dotnet $illink @args
    & pwsh -NoProfile -File (Join-Path $root "inspect.ps1") -Path (Join-Path $out "MQTTnet.dll") 2>$null | Select-String "FILE|TYPE|paramRows"
}
