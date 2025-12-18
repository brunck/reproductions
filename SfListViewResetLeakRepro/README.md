# SfListViewResetLeakRepro

Minimal .NET MAUI **Android** repro for `Syncfusion.Maui.ListView` where `ItemsGenerator.CachedTemplateViews` grows across refreshes when using `CachingStrategy="CreateNewTemplate"`.

## Environment
- .NET 9
- Target framework: `net9.0-android35.0`
- Packages (see project file for exact versions):
  - `Syncfusion.Maui.ListView` (observed: `31.2.18.0`)
  - `Syncfusion.Maui.PullToRefresh`
  - `Syncfusion.Maui.Core`

## Key files
- `MainPage.xaml`: UI (`SfPullToRefresh` + `SfListView` with `CachingStrategy="CreateNewTemplate"`)
- `MainViewModel.cs`: refresh logic (Reset via `Items.Clear()` + re-add)
- `Diagnostics/SfListViewCacheProbe.cs`: read-only probe that logs `ItemsGenerator` + `CachedTemplateViews` and samples `TemplateViewCache` entries

## What the repro does
- UI: `SfPullToRefresh` wrapping `SfListView`.
- `SfListView.CachingStrategy="CreateNewTemplate"`.
- `ItemsSource` is an `ObservableCollection<RowItem>`.
- On each refresh, the app replaces the list contents.

## Repro matrix (important)
The behavior depends on *both* the item count and how the collection is cleared:

| `ItemsPerRefresh` | Clear method | Result |
|---:|---|---|
| `1` | `Items.Clear()` (Reset) | **Leak repro**: `CachedTemplateViews` grows (e.g., `1 -> 6` after 5 refreshes) |
| `1` | remove items individually (`RemoveAt`) | **No leak**: `CachedTemplateViews` stays stable |
| `20` | `Items.Clear()` (Reset) | **No leak**: `CachedTemplateViews` stays `0 -> 0` |

This means the leak is not explained by `Clear()` alone; it appears to require the combination of **high churn** (`ItemsPerRefresh = 1`) + **Reset semantics** (`Clear()`).

## How to switch scenarios
In `MainViewModel.cs`:
- Set `ItemsPerRefresh = 1` to reproduce the leak *when using `Items.Clear()`*.
- Keep `ItemsPerRefresh = 1` but switch the clear behavior to per-item removal (remove items individually) to see the leak disappear.
- Set `ItemsPerRefresh = 20` to see the leak disappear even with `Items.Clear()`.

## What to look for (in-app diagnostics)
Use the **"List diagnostics"** button (already in the UI). It prints:
- `ItemsGenerator: Syncfusion.Maui.ListView.ItemsGenerator`
- `root.CachedTemplateViews: List\`1 (count=N)`
- a sampled list of `TemplateViewCache` entries including the retained root `View=VerticalStackLayout#...`

### Expected observation
With `ItemsPerRefresh = 1` **and `Items.Clear()`**:
- At app start: `CachedTemplateViews count=1`
- After 5 refreshes: `CachedTemplateViews count=6` (grows by ~+5)
- Sampled entries show **6 distinct** `VerticalStackLayout` identities.

With `ItemsPerRefresh = 1` **and per-item removal (`RemoveAt`)**:
- `CachedTemplateViews` stays stable (no growth after 5 refreshes).

With `ItemsPerRefresh = 20` **and `Items.Clear()`**:
- `CachedTemplateViews count=0` both at start and after 5 refreshes (stable)

## Notes
- The view model includes a diagnostic toggle `ForceGcAfterEachRefresh = true` (see `MainViewModel.cs`). Even with forced full GC after each refresh, `CachedTemplateViews` still grows for `ItemsPerRefresh = 1`, indicating strong retention rather than “GC timing”.

## Steps to reproduce
1. Build and deploy (see below).
2. Tap **"List diagnostics"** (baseline).
3. Perform **5** pull-to-refresh gestures.
4. Tap **"Run GC"** a few times.
5. Tap **"List diagnostics"** again.
6. Compare the `CachedTemplateViews` counts.

## Build + deploy
From `pwsh`:

```powershell
Set-Location c:\s\ic\SfListViewResetLeakRepro
.\scripts\build-and-deploy.ps1
```

## Optional: managed gcdump evidence
This repo includes scripts to capture and diff managed heap reports.

Prereqs:
- Android device connected via `adb`
- A way to get the **managed PID**

```powershell
Set-Location c:\s\ic\SfListViewResetLeakRepro

# Baseline
.\scripts\collect-gcdump.ps1 -ManagedPid <PID> -OutDir .\gcdumps\minimal-repro -Report

# Perform 5 refreshes in the app

# After
.\scripts\collect-gcdump.ps1 -ManagedPid <PID> -OutDir .\gcdumps\minimal-repro -Report

# Diff the two text reports
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\diff-gcdump.ps1 <beforeReportPath> <afterReportPath>

# Or use Visual Studio to compare the .gcdump files
```

In the leaking scenario (`ItemsPerRefresh = 1`, using `Items.Clear()`), the diff typically includes (after 5 refreshes):
- `Syncfusion.Maui.ListView.TemplateViewCache : 1 -> 6`
