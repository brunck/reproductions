# `Element._pendingHandlerUpdatesFromBPSet` Concurrency Repro

## Goal

Demonstrate that `Microsoft.Maui.Controls.Element` keeps stack-scoped, thread-local state in a
shared per-instance `HashSet<string>`, and that a concurrent bindable-property write therefore
corrupts framework state and throws an exception naming neither the property nor the thread.

`src/Controls/src/Core/Element/Element.cs` (MAUI `main` @ `013e8bfb1d`):

```csharp
HashSet<string> _pendingHandlerUpdatesFromBPSet = new HashSet<string>();          // :701

private protected override void OnBindablePropertySet(BindableProperty property, object original,
    object value, bool changed, bool willFirePropertyChanged)
{
    if (willFirePropertyChanged)
        _pendingHandlerUpdatesFromBPSet.Add(property.PropertyName);              // :706

    base.OnBindablePropertySet(property, original, value, changed, willFirePropertyChanged);
    _pendingHandlerUpdatesFromBPSet.Remove(property.PropertyName);               // :710
    UpdateHandlerValue(property.PropertyName, changed);
}

protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
{
    bool waitForHandlerUpdateToFireFromBP =
        _pendingHandlerUpdatesFromBPSet.Contains(propertyName);                  // :730
    ...
}
```

The `Add` → `base` → `Remove` bracket is strictly synchronous on one thread. The set is a *field*
only because the flag must survive a virtual `OnPropertyChanged` that subclasses may override.
Two threads calling `SetValue` on the same `Element` mutate it concurrently, and a corrupted
`HashSet` surfaces later as `IndexOutOfRangeException` (in `AddIfNotPresent`) or
`InvalidOperationException` ("non-concurrent... exclusive access", in `Remove`).

A single-threaded `HashSet` essentially never throws either exception, which is what makes these
two signatures diagnostic of corruption rather than of a logic error.

## Environment

- MAUI: **10.0.90**, pinned via `<MauiVersion>` in
  [BindablePropertyConcurrencyRepro.csproj](BindablePropertyConcurrencyRepro.csproj)
- Target frameworks: `net10.0-android`, `net10.0-ios` only. Windows and MacCatalyst are
  deliberately not targeted — the defect is in platform-agnostic `Controls` code.
- No `global.json`, no private feeds. Builds from a fresh clone with any .NET 10 SDK that has the
  `android` / `ios` workloads.

## Repro Steps

1. Build and deploy to a physical Android device (or a simulator/device for iOS).
2. Press **A1**. Each button runs a bounded stress loop (30 s) against a single shared element and
   reports **CLEAN** or **THREW** in the status label, with the caught exception's full
   `ToString()` rendered below it. Failure time is stamped at the throw site, not when the loops
   finish unwinding.

| Button | What it does |
| --- | --- |
| **A1 — Reduced, no platform interaction** | Two `Task.Run` loops writing two different *custom* bindable properties on the same element (see [RaceLabel.cs](RaceLabel.cs)). Neither is in any platform property mapper, so no platform view is touched on any thread. |
| **A2 — Same, platform-mapped properties** | Identical shape against `Opacity` and `CharacterSpacing` — our production shape. |
| **B — Realistic** | `Label.Text` bound to a plain-INPC view model, notifications raised from a background loop, while a fade animation runs on that same element. |
| **C — Custom BP callback** | [`RaceControl`](RaceControl.cs)'s `IsBusy` callback writes `BackgroundColor` on itself, driven from a bound VM property, with a fade animation on the same element. |
| **D1 — Unmarshalled framework path** | **One** background loop calling `VisualStateManager.GoToState`, racing a fade animation driven by the framework's own ticker on the UI thread. The app makes exactly one off-thread call. |
| **D2 — Same, two off-thread writers** | `VisualStateManager.GoToState` *and* a `Style` assignment, both from background threads, plus the fade animation. Kept for comparison. |

**A1 is the isolation; D1 is the complaint.**

A1 deliberately has the app write two bindable properties concurrently. That is the *app's* mistake,
and A1's only job is to show the mechanism with nothing else in the frame — no platform view, no
binding engine, no animation.

**D1 is the case that does not require the app to do anything unusual.** The app calls exactly one
off-thread API: `VisualStateManager.GoToState`. That method is not documented as UI-thread-only, it
takes no dispatcher, it does not marshal, and driving it from view-model state is an ordinary MAUI
pattern. The only other writer is the framework's own animation ticker on the UI thread. So the two
concurrent writes to `Element._pendingHandlerUpdatesFromBPSet` are one app call and one framework
subsystem — which makes "don't write properties from a background thread" an incomplete answer,
because there is no documented rule the app broke.

A1 exists in that form because **A2 alone is not sufficient evidence**: `Opacity` and
`CharacterSpacing` are platform-mapped, so `Element.UpdateHandlerValue` reaches the platform view
and Android's `ViewRootImpl.checkThread` can throw first, masking the framework defect behind
`CalledFromWrongThreadException`. A1
removes the platform from the picture entirely: every frame between the app and the `HashSet` is
platform-agnostic `Controls` code.

## Expected vs Actual

- **Expected:** an off-thread bindable-property write may lose an update or produce a platform
  thread-affinity error, but must not corrupt framework-internal state or throw from a `HashSet`
  operation unrelated to the property being written.
- **Actual:** `IndexOutOfRangeException` / `InvalidOperationException` from inside
  `HashSet<string>` on the `Element.OnBindablePropertySet` path.

## Observed results

Pixel 8a, Android 16, 9 cores · **Release** build (trimmed + AOT, matching the configuration our
production crash occurred in) · `Microsoft.Maui.Controls 10.0.90+8e2547a4707f745a27a7791495b240e756926980`.

| Scenario | Result | Time to failure | Exception |
| --- | --- | --- | --- |
| **D1** | **THREW**, 3/3 consecutive runs from a fresh process | 0.68 s, 0.56 s, 0.67 s | `InvalidOperationException` ×2 and `IndexOutOfRangeException` ×1, all in `HashSet.AddIfNotPresent`; thrown on the background thread each time |
| **A1** | **THREW**, 3/3 consecutive runs from a fresh process | 0.02 s, 0.03 s, 0.02 s | `IndexOutOfRangeException` and `InvalidOperationException`, both in `HashSet.AddIfNotPresent` |
| **A2** | **THREW** | 0.03 s | `IndexOutOfRangeException` in `HashSet.AddIfNotPresent` |
| **B** | clean | — | ran 30.2 s with no exception |
| **C** | clean | — | ran 30.3 s with no exception |
| **D2** | **THREW** | 0.03 s | `IndexOutOfRangeException` in `HashSet.AddIfNotPresent`, via `VisualStateManager.GoToState` → `Setter.UnApply` → `ClearValue` |

Both production signatures appear across three runs of **D1 alone**, and again across three runs of
A1, all from the same unchanged binary. That is the point: one root cause surfaces as two unrelated-
looking exceptions, neither of which names the property, the thread, or the corrupted collection.

D1's three runs were each thrown on the background `GoToState` thread (managed id 4) while the
animation ran on the UI thread. **B staying clean is consistent with the binding engine marshalling
off-thread applies to the UI thread** — this report makes no claim of a marshalling gap there.

<details>
<summary>A1 — <code>IndexOutOfRangeException</code></summary>

```
System.IndexOutOfRangeException: Arg_IndexOutOfRangeException
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String , Int32& )
   at System.Collections.Generic.HashSet`1[[System.String, ...]].Add(String )
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(BindableProperty property, BindablePropertyContext context, Object value, Boolean currentlyApplying, SetValueFlags attributes, SetterSpecificity specificity, Boolean silent)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(BindableProperty property, Object value, SetValueFlags attributes, SetValuePrivateFlags privateAttributes, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindableProperty property, Object value)
   at BindablePropertyConcurrencyRepro.RaceLabel.set_RaceBeta(Int32 value)
```
</details>

<details>
<summary>A1 — <code>InvalidOperationException</code>, same binary, same button</summary>

```
System.InvalidOperationException: InvalidOperation_ConcurrentOperationsNotSupported
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String , Int32& )
   at System.Collections.Generic.HashSet`1[[System.String, ...]].Add(String )
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(...)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(...)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindableProperty property, Object value)
   at BindablePropertyConcurrencyRepro.RaceLabel.set_RaceBeta(Int32 value)
```
</details>

<details>
<summary>D1 — via <code>VisualStateManager.GoToState</code>, one off-thread call</summary>

```
System.IndexOutOfRangeException: Arg_IndexOutOfRangeException
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String , Int32& )
   at System.Collections.Generic.HashSet`1[[System.String, ...]].Add(String )
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.ClearValueCore(BindableProperty property, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.ClearValue(BindableProperty property, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.Setter.UnApply(BindableObject target, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.VisualStateManager.GoToState(VisualElement visualElement, String name)
```
</details>

## Suggested direction

The intended lifetime of this state is one synchronous call on one thread, so the natural fix is to
stop storing it per-instance. Thread-local storage removes the race by construction — no lock, no
contention — and also removes one `HashSet<string>` allocation per `Element`, which is already
scoped as **DS06** in the memory-allocation issue (dotnet/maui#34149).

One interaction is worth surfacing rather than glossing over, because it shapes what the right
storage actually is: **the `Remove` at `Element.cs:710` is unconditional.** It runs even when
`willFirePropertyChanged` was false and nothing was added, so the marker is already unbalanced with
respect to re-entrancy — a nested `SetValue` for the same property name clears the outer call's
marker today, single-threaded, per element. A bare `[ThreadStatic] static HashSet<string>` would fix
the race and the allocation but widen that existing aliasing from *the same element* to *any element
on that thread*. Storing a depth count, or keying on the `(element, property)` pair, would avoid the
race without widening it.

Two sibling collections on `BindableObject` appear to follow the same pattern, but nothing here
reproduces against them and this repro makes no claim about them.

## Evidence capture

[ExceptionReporter.cs](ExceptionReporter.cs) hooks `AppDomain.CurrentDomain.UnhandledException`,
`TaskScheduler.UnobservedTaskException` and (on Android)
`AndroidEnvironment.UnhandledExceptionRaiser`, so the stack survives the process being killed:

- Every report is written to the platform log prefixed `BPRACE` — `adb logcat | Select-String BPRACE`.
- It is also appended to `last-crash.txt` in the app data directory and rendered on the next
  launch, so a crash that kills the process is still readable on-device.

## Notes

- No DI, no services, no third-party libraries.
- The app targets one `Label` and one custom `ContentView`; every scenario resets them first.
- `MauiXamlInflator` is left at the default (runtime/XamlC) rather than the template's `SourceGen`,
  to keep XAML codegen out of the variables under test.
