<#
Collect a managed gcdump given the managed PID (as printed by dsrouter).

Usage:
  .\scripts\collect-gcdump.ps1 -ManagedPid 14116
  .\scripts\collect-gcdump.ps1 -ManagedPid 14116 -OutDir C:\temp\gcdumps -Report

Notes:
- This script expects `dotnet-gcdump` to be installed and available in PATH.
- Copy the managed PID from dsrouter and pass it to this script.
#>

param(
    [Parameter(Mandatory=$true)]
    [int]$ManagedPid,

    [string]$OutDir = "$(Join-Path $PWD 'gcdumps')",

    [switch]$Report
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet-gcdump not found in PATH. Install dotnet-gcdump or add it to PATH."
    exit 1
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$gcdumpFile = Join-Path $OutDir "gcdump-$timestamp-$ManagedPid.gcdump"

Write-Host "Collecting gcdump for managed PID $ManagedPid -> $gcdumpFile"

try {
    dotnet-gcdump collect -p $ManagedPid -o $gcdumpFile
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
