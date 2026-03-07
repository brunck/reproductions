<#
Collect a managed gcdump given the managed PID (as printed by dsrouter).

Usage:
  .\scripts\collect-gcdump.ps1 -ManagedPid 14116
  .\scripts\collect-gcdump.ps1 -ManagedPid 14116 -OutDir C:\temp\gcdumps -Report

Notes:
- This script expects `dotnet-gcdump` to be installed and available in PATH.
  Install it with: dotnet tool install --global dotnet-gcdump
- To get the managed PID, run `dsrouter` separately (it prints the PID once the app connects).
  Example dsrouter command:
    dsrouter client-server --server-connect 127.0.0.1:9000 --client-listen 127.0.0.1:9001 --forward-diagnostics android
  Then in another terminal:
    adb forward tcp:9000 tcp:9000
  Copy the managed PID from dsrouter output and pass it here.
#>

param(
    [Parameter(Mandatory=$true)]
    [int]$ManagedPid,

    [string]$OutDir = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'gcdumps'),

    [switch]$Report
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet-gcdump not found in PATH. Install with: dotnet tool install --global dotnet-gcdump"
    exit 1
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$gcdumpFile = Join-Path $OutDir "gcdump-$timestamp-pid$ManagedPid.gcdump"

Write-Host "Collecting gcdump for managed PID $ManagedPid -> $gcdumpFile"

try {
    dotnet-gcdump collect -p $ManagedPid -o $gcdumpFile

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-gcdump exited with code $LASTEXITCODE"
    }

    Write-Host "gcdump collected: $gcdumpFile"

    if ($Report.IsPresent) {
        $reportFile = [System.IO.Path]::ChangeExtension($gcdumpFile, '.txt')
        Write-Host "Generating textual report: $reportFile"
        dotnet-gcdump report $gcdumpFile > $reportFile
        Write-Host "Report written: $reportFile"
    }
}
catch {
    Write-Error "Failed to collect gcdump: $($_.Exception.Message)"
    exit 1
}
