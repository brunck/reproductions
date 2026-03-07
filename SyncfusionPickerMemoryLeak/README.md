# SfPicker Memory Leak — ThemeElement.elements Static Retention

## Summary

`Syncfusion.Maui.Picker.SfPicker` (v32.2.5) registers internal child controls
(e.g. `PickerColumnHeaderView`) in a static `Object[]` field —
`Syncfusion.Maui.Themes.ThemeElement.elements`. There is no public API to unregister from this
list, and Syncfusion's cleanup is incomplete — one entry always persists after disposal,
keeping the most recently disposed page alive indefinitely.

When pages containing `SfPicker` are navigated away from and disposed, at least one internal
control remains in the static array. Its event handlers keep the parent picker alive, which
keeps the entire page and ViewModel alive — **the last disposed page always leaks**.

Even though only **1 entry** is observed in `ThemeElement.elements`, it roots a massive object
graph. In the repro app after 5 navigation cycles: **+9,483 leaked objects, +676 KB heap growth**.
In a production app over 10 cycles: **+25,036 leaked objects, +1.95 MB heap growth**.

---

## Environment

| Component | Version |
|-----------|---------|
| .NET SDK | 10.0.103 |
| `Syncfusion.Maui.Picker` | 32.2.5 |
| `Microsoft.Maui.Controls` | 10.0.41 |
| Target | net10.0-android (API 35+) |

---

## Steps to Reproduce

### Build and Deploy

```powershell
.\scripts\build-and-deploy.ps1           # Release build (default)
.\scripts\build-and-deploy.ps1 -Configuration Debug
```

**Prerequisites:** .NET SDK 10.0.103 with `maui-android` workload, `adb` in PATH,
Android device with USB debugging enabled.

### Run the Test

1. Launch the app on device.
2. Tap **"Auto-Run 5 Cycles"**. This will:
   - Record baseline `ThemeElement.elements` count
   - Loop 5 times: push a modal `TabbedPage` containing a `PickerPage` with 2 `HourMinutePicker`
     instances, call `DisposeAndClearChildren()`, pop the modal
   - Force 3x GC
   - Display the final inspection

### Manual Cycle (one cycle)

1. Tap **"One Cycle (push modal + pop)"** — pushes a modal `DeviceDetailTabbedPage`,
   waits 500ms, calls `DisposeAndClearChildren()`, pops.
2. Repeat 5 times.
3. Tap **"Force GC"**.
4. Tap **"Inspect ThemeElement.elements"**.

---

## Expected Behavior

After `PopModalAsync` + GC, `ThemeElement.elements` should not retain references to controls
from disposed pages. `WeakReference` targets for disposed `DeviceDetailTabbedPage` instances
should report `collected (OK)`.

---

## Actual Behavior

`ThemeElement.elements` grows from 0 to 1 on the first cycle and stays at 1 thereafter — it
appears Syncfusion replaces/reuses the entry rather than appending. However, that **single entry**
is sufficient to root the last disposed page's entire object graph via event handler chains.

After 5 auto-run cycles + GC:
- `ThemeElement.elements` count: **1** (delta +1 from baseline of 0)
- Pages 1–4: **collected (OK)** — properly GC-ed
- Page 5 (last): **ALIVE (leaked!)** — rooted by the static array

GC dump comparison (baseline → after 5 cycles):

| Metric | Baseline | After | Delta |
|--------|----------|-------|-------|
| Heap Bytes | 1,801,064 | 2,477,720 | **+676,656** |
| Heap Objects | 24,772 | 34,255 | **+9,483** |

Key retained types (not present in baseline at all):

| Count | Type |
|-------|------|
| 6 | Syncfusion.Maui.Picker.PickerColumnHeaderView |
| 4 | Syncfusion.Maui.Picker.PickerSelectionView |
| 4 | Syncfusion.Maui.Picker.PickerHeaderView |
| 4 | Syncfusion.Maui.Picker.PickerColumn |
| 19 | Syncfusion.Maui.Picker.PickerTextStyle |
| 2 | Syncfusion.Maui.Picker.PickerContainer |
| 2 | Syncfusion.Maui.Picker.PickerStackLayout |
| 2 | SyncfusionPickerMemoryLeak.HourMinutePicker |
| 1 | SyncfusionPickerMemoryLeak.PickerPage |
| 1 | SyncfusionPickerMemoryLeak.DeviceDetailTabbedPage |
| 10 | EventHandler\<PickerPropertyChangedEventArgs\> |
| 541 | Microsoft.Maui.Controls.BindableObject.BindablePropertyContext |

---

## Root Cause

`ThemeElement` maintains a static `Object[]` (`elements`). Every theme-aware Syncfusion control
registers itself in this array during construction so theme changes can be broadcast.

1. Registration happens automatically in the control constructor.
2. There is no `Unregister` or `RemoveElement` API.
3. `DisconnectHandler()` and `Dispose()` do not fully clean up `elements` — one entry always
   persists after the cleanup cycle completes, regardless of how many cycles have run.
4. Strong (not weak) references mean the persisting entry keeps its control alive.
5. That single surviving entry roots the last disposed page's entire object graph through
   event handler chains (see reference chain below).

### Reference Chain (from GC Paths to Root, repro app)

The following is the actual GC path to root for `PlaceholderTabPage` from the repro app gcdumps
(read bottom-up: `ThemeElement.elements` is the GC root, `PlaceholderTabPage` is what it keeps alive):

```
ThemeElement.elements (static Object[])
  → PickerColumnHeaderView
    → EventHandler<PickerPropertyChangedEventArgs>
      → HourMinutePicker
        → EventHandler<PickerSelectionChangedEventArgs>
          → PickerPage
            → PropertyChangedEventHandler
              → DeviceDetailTabbedPage
                → Dictionary<BindableProperty, BindablePropertyContext>
                  → BindablePropertyContext → SetterSpecificityList<Object> → Object[]
                    → NavigationProxy
                      → NavigationPage.MauiNavigationImpl
                        → NavigationPage
                          → NavigationPageToolbar
                            → PlaceholderTabPage
```

The key links are the event handler chains: `PickerColumnHeaderView` → (via `PickerPropertyChangedEventArgs` handler) → `HourMinutePicker` → (via `PickerSelectionChangedEventArgs` handler) → `PickerPage` → (via `PropertyChangedEventHandler`) → `DeviceDetailTabbedPage` and everything inside the modal `NavigationPage` wrapping it.

### Why Modal TabbedPage is the Critical Pattern

When pages are popped from a `NavigationPage` stack, MAUI automatically disconnects handlers,
which may give Syncfusion's cleanup a better opportunity to run. But when tabs live inside a
**modal `TabbedPage`**, MAUI does NOT auto-disconnect handlers on `PopModalAsync`. Manual
`DisconnectHandler()` calls are required and still leave one `ThemeElement.elements` entry
behind — always pointing to the most recently disposed page, keeping it unfreeable.

---

## Workarounds Attempted

| Attempt | Result |
|---------|--------|
| Call `picker.Handler?.DisconnectHandler()` on cleanup | No effect on `ThemeElement.elements` |
| Set `BindingContext = null` before disposal | No effect |
| Clear picker columns before disposal | No effect |
| Null `Content` and `BindingContext` on parent pages | No effect |

---

## Suggested Fix

Any of the following would resolve the issue:

1. **Preferred:** Store `WeakReference<object>` instead of strong references in `elements`.
   Live controls still receive theme updates; disposed controls can be GC-ed.

2. Remove entries from `elements` when `IView.DisconnectHandler` is called.

3. Expose a public static `ThemeElement.Unregister(object element)` API.

---

## Reading the Diagnostic Output

- **ThemeElement.elements count** — should return to 0 after GC if no leak. Count of 1 = leaked.
- **Delta from baseline** — any growth indicates retained controls from disposed pages.
- **WeakRef status** — `ALIVE (leaked!)` = page is rooted by the static array; `collected (OK)` = freed.
- **gcdump comparison** — run `python compare_gcdumps.py baseline.txt after.txt` for detailed per-type analysis.

---

## Optional: gcdump Collection

```powershell
dotnet tool install --global dotnet-gcdump

# Terminal 1: port forward + dsrouter
adb forward tcp:9000 tcp:9000
dsrouter client-server --server-connect 127.0.0.1:9000 --client-listen 127.0.0.1:9001 --forward-diagnostics android

# Terminal 2: collect
.\scripts\collect-gcdump.ps1 -ManagedPid <pid> -Report
```

Open the `.gcdump` in Visual Studio (**Debug → Windows → Heap Snapshot**) to see the full
object retention graph rooted at `ThemeElement.elements`.
