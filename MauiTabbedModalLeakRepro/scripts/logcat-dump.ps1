[CmdletBinding()]
param(
    [string] $OutFile = "..\logs\logcat.txt"
)

$ErrorActionPreference = 'Stop'

$dir = Split-Path -Parent $OutFile
if ($dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Write-Host "Dumping adb logcat -> $OutFile" -ForegroundColor Cyan
& adb logcat -d | Out-File -FilePath $OutFile -Encoding utf8
