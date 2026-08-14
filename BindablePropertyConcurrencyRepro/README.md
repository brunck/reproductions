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
2. Press **A**, then **B**. Each button runs a bounded stress loop (10 s) against a freshly built
   `Label` and reports **CLEAN** or **THREW** in the status label, with the caught exception's full
   `ToString()` rendered below it. Failure time is stamped at the throw site, not when the loops
   finish unwinding.

Both buttons can be pressed repeatedly; each press is independent. Every run builds a new target
element rather than reusing one, because the race leaves the framework's per-element state
corrupted.

| Button | What it does |
| --- | --- |
| **A — Isolated mechanism** | Two `Task.Run` loops writing two different *custom* bindable properties on the same element (see [RaceLabel.cs](RaceLabel.cs)). Neither property is in any platform property mapper, so no platform view is touched on any thread. |
| **B — Unmarshalled framework path** | **One** background loop calling `VisualStateManager.GoToState`, racing a fade animation driven by the framework's own ticker on the UI thread. The app makes exactly one off-thread call. |

**A is the isolation; B is the complaint.**

A deliberately has the app write two bindable properties concurrently. That is the *app's*
mistake, and A's only job is to show the mechanism with nothing else in the frame — no platform
view, no binding engine, no animation. A uses custom, unmapped properties (rather than a mapped
one like `Opacity`) because a mapped property can hit a platform thread-affinity check — Android's
`ViewRootImpl.checkThread`, iOS's `UIKitThreadAccessException` — before the write ever reaches the
vulnerable `HashSet` code, which would mask the framework defect behind a platform error instead.

**B is the case that does not require the app to do anything unusual.** The app calls exactly one
off-thread API: `VisualStateManager.GoToState`. That method is not documented as UI-thread-only, it
takes no dispatcher, it does not marshal, and driving it from view-model state is an ordinary MAUI
pattern. The only other writer is the framework's own animation ticker on the UI thread. So the two
concurrent writes to `Element._pendingHandlerUpdatesFromBPSet` are one app call and one framework
subsystem — which makes "don't write properties from a background thread" an incomplete answer,
because there is no documented rule the app broke. B also happens to use a *mapped* property
(`TextColor`, via `Setter.Apply`/`UnApply`), which is exactly what lets it demonstrate the
platform-masking behavior described above: on Android the race wins and the real `HashSet`
corruption surfaces; on iOS, UIKit's own thread-affinity check fires first (see the iOS results
below).

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
| **A** | **THREW**, 3/3 consecutive runs from a fresh process | 0.02 s, 0.03 s, 0.02 s | `IndexOutOfRangeException` and `InvalidOperationException`, both in `HashSet.AddIfNotPresent` |
| **B** | **THREW**, 3/3 consecutive runs from a fresh process | 0.68 s, 0.56 s, 0.67 s | `InvalidOperationException` ×2 and `IndexOutOfRangeException` ×1, all in `HashSet.AddIfNotPresent`; thrown on the background thread each time |

Both production signatures appear across three runs of **B alone**, and again across three runs of
**A**, all from the same unchanged binary. That is the point: one root cause surfaces as two
unrelated-looking exceptions, neither of which names the property, the thread, or the corrupted
collection.

B's three runs were each thrown on the background `GoToState` thread (managed id 4) while the
animation ran on the UI thread.

### iOS

Single run per scenario, physical device, `Microsoft.Maui.Controls 10.0.90`.

| Scenario | Result | Time to failure | Exception |
| --- | --- | --- | --- |
| **A** | **THREW** | 0.01 s | `InvalidOperationException` — one of the same two production signatures as Android |
| **B** | **THREW** | 0.00 s | `UIKit.UIKitThreadAccessException` |

**A corroborates cross-platform.** With no platform view in the frame, iOS produces
`InvalidOperationException` from the same `HashSet` corruption as Android — one of the same two
production signatures (`IndexOutOfRangeException` / `InvalidOperationException`) that Android's A
and B runs also produced — confirming the defect lives in platform-agnostic `Controls` code, not in
either platform's binding to it.

**B does not corroborate on iOS, and that is worth stating plainly rather than glossing over.**
`GoToState`'s state transition applies/unapplies `Setter`s that write `TextColor`, which is
platform-mapped (the captured iOS stack below shows `Setter.Apply`); on iOS that reaches `UILabel`
and UIKit's own thread-affinity check fires first, as `UIKitThreadAccessException` — before the
race inside `_pendingHandlerUpdatesFromBPSet` gets a chance to surface. Android's
`ViewRootImpl.checkThread` did not mask B (the actual `HashSet` frame came through), but iOS's
`UIKitThreadAccessException` does.

This is itself a useful contrast for the issue: **UIKit's check is a named, documented exception
that identifies the actual violation** (`UIKitThreadAccessException` — touched UI off the main
thread). MAUI's own `Controls` layer has no equivalent for
`_pendingHandlerUpdatesFromBPSet`, so when a mapped property doesn't get there first, the failure
mode is an unrelated `HashSet` exception instead. Two platforms, two very different failure
qualities, from the same unsynchronized field.

Practical upshot for anyone trying to reproduce B specifically: use a property absent from the
platform mapper (as A does), or run on Android, where the race tends to win before the platform
check does. A is the reliable, platform-independent evidence; B is Android-only in this harness.

**The AOT hypothesis in the original plan did not hold.** We expected the corruption might present
as `SIGSEGV`/`SIGABRT` with no managed stack under full AOT. Instead every iOS run produced an
ordinary caught managed exception with a normal `ToString()`, no different in kind from Android.
Recorded here so the assumption doesn't quietly persist into the issue text.

<details>
<summary>A — <code>IndexOutOfRangeException</code></summary>

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
<summary>A — <code>InvalidOperationException</code>, same binary, same button</summary>

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
<summary>B — via <code>VisualStateManager.GoToState</code>, one off-thread call</summary>

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

<details>
<summary>iOS — A, <code>InvalidOperationException</code></summary>

```
=== A1 (custom BPs) threw after 0.01s ===
Threw on thread: 3
System.InvalidOperationException: Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.
   at System.Collections.Generic.HashSet`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].AddIfNotPresent(String value, Int32& location)
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(BindableProperty property, BindablePropertyContext context, Object value, Boolean currentlyApplying, SetValueFlags attributes, SetterSpecificity specificity, Boolean silent)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(BindableProperty property, Object value, SetValueFlags attributes, SetValuePrivateFlags privateAttributes, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindableProperty property, Object value)
   at BindablePropertyConcurrencyRepro.RaceLabel.set_RaceAlpha(Int32 value) in RaceLabel.cs:line 28
   at BindablePropertyConcurrencyRepro.MainPage.<>c__DisplayClass4_0.<RunScenarioA1>b__0(CancellationToken token) in MainPage.xaml.cs:line 51
   at BindablePropertyConcurrencyRepro.RunContext.<>c__DisplayClass20_0.<Background>b__0() in RunContext.cs:line 43
```

(Remaining frames scrolled off-screen in the on-device capture; the frames above are the
diagnostic ones — same `HashSet.AddIfNotPresent` → `Element.OnBindablePropertySet` chain as
Android. Absolute source paths from the on-device text have been shortened to filenames here.)
</details>

<details>
<summary>iOS — B, masked by <code>UIKitThreadAccessException</code> (never reaches the HashSet)</summary>

```
=== D1 (1 off-thread GoToState) threw after 0.00s ===
Threw on thread: 3
UIKit.UIKitThreadAccessException: UIKit Consistency error: you are calling a UIKit method that can only be invoked from the UI thread.
   at UIKit.UIApplication.EnsureUIThread() in /Users/cloudtest/vss/_work/1/s/macios/src/UIKit/UIApplication.cs:line 133
   at UIKit.UILabel.set_TextColor(UIColor value) in /Users/cloudtest/vss/_work/1/s/macios/src/build/dotnet/ios/generated-sources/UIKit/UILabel.g.cs:line 812
   at Microsoft.Maui.Platform.LabelExtensions.UpdateTextColor(UILabel platformLabel, ITextStyle textStyle, UIColor defaultColor)
   at Microsoft.Maui.Handlers.LabelHandler.MapTextColor(ILabelHandler handler, ILabel label)
   at Microsoft.Maui.PropertyMapper`2[...].<Add>b__0(IElementHandler h, IElement v)
   at Microsoft.Maui.Controls.Label.MapTextColor(ILabelHandler handler, Label label, Action`2 baseMethod)
   at Microsoft.Maui.PropertyMapperExtensions.<>c__DisplayClass1_0`2[...].<ModifyMapping>g__newMethod|0(IElementHandler handler, IElement view)
   at Microsoft.Maui.PropertyMapper`2[...].<Add>b__0(IElementHandler h, IElement v)
   at Microsoft.Maui.PropertyMapper.TryUpdatePropertyCore(String key, IElementHandler viewHandler, IElement virtualView)
   at Microsoft.Maui.PropertyMapper.UpdateProperty(IElementHandler viewHandler, IElement virtualView, String property)
   at Microsoft.Maui.Handlers.ElementHandler.UpdateValue(String property)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(BindableProperty property, BindablePropertyContext context, Object value, Boolean currentlyApplying, SetValueFlags attributes, SetterSpecificity specificity, Boolean silent)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(BindableProperty property, Object value, SetValueFlags attributes, SetValuePrivateFlags privateAttributes, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindableProperty property, Object value, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.Setter.Apply(BindableObject target, SetterSpecificity specificity)
```

(A few generic-type-argument frames collapsed to `[...]` for readability; nothing diagnostic was in
them. This is the concrete illustration of "B is masked on iOS": the call never gets past
`UILabel.set_TextColor` to reach `Element.OnBindablePropertySet`/the `HashSet` at all, unlike
Android where the race wins and the real corruption surfaces instead.)
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
- Every run builds its own target `Label`; no state is shared between runs.
- B's throw sometimes lands on the UI thread inside the framework's animation ticker
  (`AnimationManager.OnFire` → … → `VisualElement.set_Opacity`), which is not inside any app code
  and so cannot be caught by the scenario. That is reported as **THREW (unhandled)**. Left alone it
  terminates the process; the harness marks it handled so the app stays usable, but in a real app
  this is a hard crash.
- `MauiXamlInflator` is left at the default (runtime/XamlC) rather than the template's `SourceGen`,
  to keep XAML codegen out of the variables under test.
