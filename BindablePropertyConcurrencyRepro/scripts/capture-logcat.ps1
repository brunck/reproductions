[CmdletBinding()]
param(
    # Empty = clear the buffer and stream live until Ctrl+C. Otherwise dump what is buffered.
    [switch] $Dump,

    [string] $OutFile = ''
)

$ErrorActionPreference = 'Stop'

if ($Dump) {
    if ([string]::IsNullOrWhiteSpace($OutFile)) {
        $OutFile = Join-Path $PSScriptRoot '..\logs\logcat.txt'
    }

    $dir = Split-Path -Parent $OutFile
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    Write-Host "Dumping logcat -> $OutFile" -ForegroundColor Cyan
    & adb logcat -d | Out-File -FilePath $OutFile -Encoding utf8

    Write-Host 'BPRACE / exception lines:' -ForegroundColor Yellow
    Select-String -Path $OutFile -Pattern 'BPRACE', 'HashSet', 'OnBindablePropertySet', 'AndroidRuntime'
    return
}

Write-Host 'Clearing logcat, then streaming. Ctrl+C to stop.' -ForegroundColor Cyan
& adb logcat -c
& adb logcat
