# Syncfusion SfPicker Memory Leak — Focused Reproduction App

## What This Is

A minimal .NET MAUI Android app that reproduces a memory leak in `Syncfusion.Maui.Picker.SfPicker`
v32.2.5. The purpose is a focused **Syncfusion bug report**: `ThemeElement.elements` (a static
`Object[]` in `Syncfusion.Maui.Themes`) holds strong references to every Syncfusion theme-aware
control ever created, with no removal mechanism. This prevents GC of disposed pages.

## Key Files in This Repo

| File | Purpose |
|------|---------|
| `MainPage.xaml/.cs` | Root page: auto-run 5 cycles, inspect, force GC |
| `DeviceDetailTabbedPage.cs` | Modal TabbedPage with PickerPage + PlaceholderTabPage tabs |
| `PickerPage.xaml/.cs` | 2x HourMinutePicker (dialog mode), ViewModel binding, SelectionChanged handlers |
| `HourMinutePicker.cs` | Custom SfPicker subclass — triggers ThemeElement.elements registrations |
| `PickerViewModel.cs` | INotifyPropertyChanged ViewModel |
| `ThemeElementInspector.cs` | Reflection-based reader of `ThemeElement.elements` |
| `App.xaml.cs` | Root: `NavigationPage(new MainPage())` |

## The Leak Pattern

Each cycle:
1. `PushModalAsync(new NavigationPage(new DeviceDetailTabbedPage()))` — creates picker instances, ThemeElement.elements grows
2. `tabbedPage.DisposeAndClearChildren()` — disconnects handlers but does NOT remove ThemeElement.elements entries
3. `PopModalAsync()` — MAUI does NOT auto-disconnect tab-child handlers on modal pop (unlike NavigationPage.PopAsync)

Result: ThemeElement.elements ends up with exactly 1 entry after each cleanup cycle — always
the most recently disposed picker's internal view. That 1 entry roots the last disposed page's
entire object graph indefinitely.

Reference chain:
```
ThemeElement.elements (static) → PickerColumnHeaderView
  → EventHandler<PickerPropertyChangedEventArgs> → HourMinutePicker
    → EventHandler<PickerSelectionChangedEventArgs> → PickerPage
      → PropertyChangedEventHandler → DeviceDetailTabbedPage → NavigationPage → PlaceholderTabPage
```

## Build & Deploy

Don't run scripts yourself — prompt the user to run them and report back.

```powershell
.\scripts\build-and-deploy.ps1           # Release build
.\scripts\build-and-deploy.ps1 -Configuration Debug
```

**Prerequisites**: .NET SDK 10.0.103, `maui-android` workload, `adb` in PATH, USB-debug Android device.

## Diagnostics

- **Auto-Run 5 Cycles** button: fully automated — baseline, 5 push/pop cycles, GC, inspect
- **Inspect** button: reads ThemeElement.elements, checks WeakRefs, reports verdict
- **adb logcat** tags: `[MainPage]`, `[DeviceDetailTabbedPage]`, `[PickerPage]`, `[HourMinutePicker]`

## Key Technical Facts

- `ThemeElement.elements` is `static Object[]` — strong refs, no removal API
- `DisconnectHandler()` and `Dispose()` do NOT remove entries
- `ThemeElementInspector` uses reflection (field name `elements`) — `PublishTrimmed=false` required
- MAUI version: `10.0.41`
- Syncfusion version: `32.2.5`

