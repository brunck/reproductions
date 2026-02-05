# MAUI TabbedPage Modal Leak Repro

## Goal
Minimal .NET MAUI app that demonstrates managed leak when repeatedly opening and closing a modal `TabbedPage` (with optional per-tab `NavigationPage` wrappers). Primary signal is in-app `WeakReference` tracking + forced GC; optional gcdump script is included for deeper analysis.

## Environment (fill in)
- SDK: 10.0.102 (see [global.json](global.json))
- MAUI workload version: 10.0.102
- Device/emulator: 
- Android API level: 

## Repro Steps
1. Build/deploy to Android.
2. On the main screen:
   - Tap **Run repro N times** (default 5).
3. Watch logcat for `LeakRepro` lines (or `Status` label). After each modal pop, GC runs and logs `Alive` counts.

## Expected
After modal pop + forced GC, the number of alive `LeakyTabbedModalPage` instances should return to 0 (or remain stable).

## Actual (suspected)
`Alive` counts increase by 1 each loop, indicating retained `LeakyTabbedModalPage` instances.

## Log Output Example
```
[12:34:56.789] LeakRepro | after pop 1/5 | Alive=1/4 | Types=MauiTabbedModalLeakRepro.LeakyTabbedModalPage:1,...
```

## Optional gcdump tooling
See [scripts/gcdump.ps1](scripts/gcdump.ps1) for optional collection (requires `dotnet-gcdump`).

## Helper scripts
- [scripts/build-android.ps1](scripts/build-android.ps1)
- [scripts/run-android.ps1](scripts/run-android.ps1)
- [scripts/logcat-clear.ps1](scripts/logcat-clear.ps1)
- [scripts/logcat-dump.ps1](scripts/logcat-dump.ps1)

## Notes
- The repro intentionally avoids DI, services, and 3rd-party libraries.
- Toggle **Wrap tabs in NavigationPage** to compare variants.
