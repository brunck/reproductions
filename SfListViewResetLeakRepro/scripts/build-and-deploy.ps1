<#
Builds a Release Android APK for the SfListViewResetLeakRepro project and installs
it on the first connected Android device using adb.

Usage: run from PowerShell (pwsh):
  .\scripts\build-and-deploy.ps1

Notes:
- Requires dotnet SDK + MAUI workloads that target net9.0-android.
- Requires `adb` (Android SDK platform-tools) available in PATH.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Resolve-Path (Join-Path $scriptDir "..")
    $projectFile = Join-Path $projectRoot "SfListViewResetLeakRepro.csproj"

    if (-not (Test-Path $projectFile)) {
        throw "Project file not found: $projectFile"
    }

    $tfm = 'net9.0-android35.0'
    $config = 'Release'

    Write-Host "Publishing $projectFile for $tfm ($config)..."

    $publishDir = Join-Path $projectRoot "bin\$config\$tfm\publish"

    dotnet publish $projectFile -f $tfm -c $config -p:AndroidPackageFormat=apk -p:AndroidEnableProfiling=true -p:DebugType=embedded -o $publishDir --nologo

    Write-Host "Publish completed. Searching for APK in: $publishDir"

    $apk = Get-ChildItem -Path $publishDir -Filter *.apk -Recurse -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1

    if (-not $apk) {
        $apk = Get-ChildItem -Path (Join-Path $projectRoot 'bin') -Filter *.apk -Recurse -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending |
               Select-Object -First 1
    }

    if (-not $apk) {
        throw "No APK found after publish. Looked in $publishDir and bin subfolders."
    }

    Write-Host "Found APK: $($apk.FullName)"

    $adb = 'adb'
    $adbVersion = & $adb version

    Write-Host "ADB available: $adbVersion"

    $devicesOutput = & $adb devices
    $deviceLines = @($devicesOutput -split "`n" | Select-Object -Skip 1 | Where-Object { $_ -and ($_ -match '\tdevice$') })

    if (-not $deviceLines -or $deviceLines.Count -eq 0) {
        throw "No connected Android device found. Make sure adb devices shows a device in 'device' state.`n$devicesOutput"
    }

    Write-Host "Installing APK to device(s)..."

    foreach ($line in $deviceLines) {
        $deviceId = ($line -split "`t")[0]
        Write-Host "Installing to device: $deviceId"

        $packageId = 'com.companyname.sflistviewresetleakrepro'

        # Use -s to guarantee we install/launch on the intended device.
        $installArgs = @('-s', $deviceId, 'install', '-r', '-d', $apk.FullName)
        $proc = Start-Process -FilePath $adb -ArgumentList $installArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput temp_stdout.txt -RedirectStandardError temp_stderr.txt

        $stdout = Get-Content temp_stdout.txt -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content temp_stderr.txt -Raw -ErrorAction SilentlyContinue
        Remove-Item temp_stdout.txt,temp_stderr.txt -ErrorAction SilentlyContinue

        if ($proc.ExitCode -ne 0 -or ($stdout -notmatch 'Success') -or ($stdout -match 'Failure')) {
            Write-Host "adb install failed for device $deviceId (exit $($proc.ExitCode))"
            if ($stdout) { Write-Host "STDOUT:`n$stdout" }
            if ($stderr) { Write-Host "STDERR:`n$stderr" }
            throw "adb install failed"
        }

        Write-Host "Install output for $($deviceId):`n$stdout"

        # Verify the package is installed.
        $packages = & $adb -s $deviceId shell pm list packages $packageId
        if (-not ($packages -match $packageId)) {
            Write-Host "Package not found after install. pm output:`n$packages"
            throw "Install did not result in an installed package: $packageId"
        }

        # Launch the app so it's immediately visible/runnable.
        Write-Host "Launching $packageId on $deviceId..."
        & $adb -s $deviceId shell monkey -p $packageId -c android.intent.category.LAUNCHER 1 | Out-Null
    }

    Write-Host "APK installed to connected device(s) successfully."
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
