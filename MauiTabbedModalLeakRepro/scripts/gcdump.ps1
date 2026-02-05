[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int] $ManagedPid,

    [string] $OutDir = "",

    [switch] $Report
)

$ErrorActionPreference = 'Stop'

# Default OutDir to one level up from the script file, unless the caller passed -OutDir
if (-not $PSBoundParameters.ContainsKey('OutDir') -or [string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $PSScriptRoot '..\gcdumps'
}

if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) {
    throw "dotnet-gcdump not found. Install with: dotnet tool install --global dotnet-gcdump"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dumpPath = Join-Path $OutDir "gcdump-$timestamp.gcdump"

Write-Host "Collecting gcdump from PID $ManagedPid -> $dumpPath" -ForegroundColor Cyan
& dotnet-gcdump collect -p $ManagedPid -o $dumpPath

if ($Report) {
    $reportPath = [System.IO.Path]::ChangeExtension($dumpPath, ".txt")
    Write-Host "Writing report -> $reportPath" -ForegroundColor Cyan
    & dotnet-gcdump report $dumpPath | Out-File -FilePath $reportPath -Encoding utf8
}
