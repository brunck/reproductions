[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host "Clearing adb logcat..." -ForegroundColor Cyan
& adb logcat -c
