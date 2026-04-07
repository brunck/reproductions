<#
Builds a Release Android APK for the SyncfusionPickerMemoryLeak project and installs
it on the first connected Android device using adb.

Usage: run from PowerShell (pwsh):
  .\scripts\build-and-deploy.ps1
  .\scripts\build-and-deploy.ps1 -Configuration Debug
  .\scripts\build-and-deploy.ps1 -NoBuild   # skip build, install and launch last APK

Notes:
- Requires dotnet SDK 10.0.103 + MAUI workloads that target net10.0-android.
- Requires `adb` (Android SDK platform-tools) available in PATH.
- AndroidEnableProfiling is controlled by the .csproj (set it there per configuration as needed).
#>

param(
    [Alias('c')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Resolve-Path (Join-Path $scriptDir "..\SyncfusionPickerMemoryLeak")
    $projectFile = Join-Path $projectRoot "SyncfusionPickerMemoryLeak.csproj"

    if (-not (Test-Path $projectFile)) {
        throw "Project file not found: $projectFile"
    }

    $binDir = Join-Path $projectRoot "bin"

    if (-not $NoBuild) {
        Write-Host "Cleaning $projectFile"
        $objDir = Join-Path $projectRoot "obj"
        if (Test-Path $binDir) { Remove-Item $binDir -Recurse -Force }
        if (Test-Path $objDir) { Remove-Item $objDir -Recurse -Force }

        Write-Host "Publishing $projectFile"
        Write-Host "  Config : $Configuration"

        dotnet publish $projectFile -c $Configuration --nologo

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }

        Write-Host "Publish completed. Searching for APK under: $binDir"
    } else {
        Write-Host "Skipping build. Searching for existing APK under: $binDir"
    }

    $apk = Get-ChildItem -Path $binDir -Filter *.apk -Recurse -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1

    if (-not $apk) {
        throw "No APK found. Searched under $binDir"
    }

    Write-Host "Found APK: $($apk.FullName)"

    $adb = 'adb'
    $adbVersion = & $adb version
    Write-Host "ADB: $adbVersion"

    $devicesOutput = & $adb devices
    $deviceLines = @($devicesOutput -split "`n" | Select-Object -Skip 1 | Where-Object { $_ -and ($_ -match '\tdevice$') })

    if (-not $deviceLines -or $deviceLines.Count -eq 0) {
        throw "No connected Android device found. Make sure 'adb devices' shows a device in 'device' state.`n$devicesOutput"
    }

    $packageId = 'com.companyname.syncfusionpickermemoryleak'

    foreach ($line in $deviceLines) {
        $deviceId = ($line -split "`t")[0]
        Write-Host "Installing to device: $deviceId"

        $installArgs = @('-s', $deviceId, 'install', '-r', '-d', $apk.FullName)
        $proc = Start-Process -FilePath $adb -ArgumentList $installArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput temp_stdout.txt -RedirectStandardError temp_stderr.txt

        $stdout = Get-Content temp_stdout.txt -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content temp_stderr.txt -Raw -ErrorAction SilentlyContinue
        Remove-Item temp_stdout.txt, temp_stderr.txt -ErrorAction SilentlyContinue

        if ($proc.ExitCode -ne 0 -or ($stdout -notmatch 'Success') -or ($stdout -match 'Failure')) {
            Write-Host "adb install failed for device $deviceId (exit $($proc.ExitCode))"
            if ($stdout) { Write-Host "STDOUT:`n$stdout" }
            if ($stderr) { Write-Host "STDERR:`n$stderr" }
            throw "adb install failed"
        }

        Write-Host "Install output for ${deviceId}:`n$stdout"

        # Verify the package landed.
        $packages = & $adb -s $deviceId shell pm list packages $packageId
        if (-not ($packages -match $packageId)) {
            Write-Host "Package not found after install. pm output:`n$packages"
            throw "Install did not result in an installed package: $packageId"
        }

        # Launch the app so it's ready to use immediately.
        Write-Host "Launching $packageId on $deviceId..."
        & $adb -s $deviceId shell monkey -p $packageId -c android.intent.category.LAUNCHER 1 | Out-Null
    }

    Write-Host "APK installed and launched on connected device(s)."
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
