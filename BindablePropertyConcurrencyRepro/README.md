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

Physical device, `Microsoft.Maui.Controls 10.0.90`. The first two rows are single runs; the rest are
repeated-press stress tests.

| Scenario | Result | Time to failure | Exception |
| --- | --- | --- | --- |
| **A** | **THREW** | 0.01 s | `InvalidOperationException` — one of the same two production signatures as Android |
| **B** | **THREW**, caught | 0.00 s | `UIKit.UIKitThreadAccessException`, on the background `GoToState` thread |
| **B** | **THREW**, caught, ×25 consecutive presses from a fresh launch — no termination | 0.00 s each | `UIKit.UIKitThreadAccessException` every time; B alone did **not** terminate the process |
| **A**/**B** alternating, 5 presses each | **PROCESS TERMINATED**, uncaught | — | `IndexOutOfRangeException` in `HashSet.AddIfNotPresent`, **on thread 1**, in the **layout pass** |
| **B** (earlier harness build) | **PROCESS TERMINATED**, uncaught | — | `InvalidOperationException` in `HashSet.AddIfNotPresent`, **on thread 1**, in the **animation ticker** |

**A corroborates cross-platform.** With no platform view in the frame, iOS produces
`InvalidOperationException` from the same `HashSet` corruption as Android — one of the same two
production signatures (`IndexOutOfRangeException` / `InvalidOperationException`) that Android's A
and B runs also produced — confirming the defect lives in platform-agnostic `Controls` code, not in
either platform's binding to it.

**B corroborates on iOS too, but asymmetrically.** `GoToState`'s state transition applies/unapplies
`Setter`s that write `TextColor`, which is platform-mapped; on iOS that reaches `UILabel`, so UIKit's
own thread-affinity check fires on the background thread as `UIKitThreadAccessException`, and the
scenario catches it at 0.00 s. That much was expected.

What the check does **not** do is prevent the corruption. The off-thread `SetValue` completes its
`Add`/`Remove` on `_pendingHandlerUpdatesFromBPSet` *before* UIKit stops it — the caught trace below
shows the throw arriving at `UILabel.set_TextColor` from `ElementHandler.UpdateValue`, which is
downstream of that bracket. The collection is reached and mutated off-thread; the platform check
only decides what the *calling* thread sees afterwards.

**This UI-thread termination is the worst failure in the repro, and it has been observed on two
independent paths** — which matters more than either path alone, because it shows the corruption is
not tied to any particular framework subsystem. It is tied to the element: whatever the UI thread
touches that element with next is what dies.

One honest qualification before the detail: the terminations were reached with **A** in the mix, and
**A** is what corrupts the set. B's own off-thread write is stopped by UIKit after a single
iteration, which is why B alone never terminated the process (see reproducibility below). What B
contributes on iOS is the demonstration that a platform thread-affinity check does not protect the
collection — not the kill itself.

| Path | UI-thread writer | Driven by |
| --- | --- | --- |
| **Layout** | `VisualElement.set_Width`, via `set_Frame` → `UpdateBoundsComponents` | UIKit calling `ContentView.LayoutSubviews()` to arrange the element |
| **Animation** | `VisualElement.set_Opacity` | `AnimationManager.OnFire` → `PlatformTicker` |

The layout path is the harder of the two to argue away:

- **`Width` is a read-only bindable property.** The frame is `SetValue(BindablePropertyKey, Object)`
  — the *key* overload. An app cannot write `Width`; only the framework can. The UI-thread writer
  here is unambiguously MAUI's own layout system.
- **A layout pass is not optional.** Starting an animation is something an app chose to do. Being
  arranged is not: every element that appears on screen goes through `ArrangeOverride`, and here it
  is driven directly by UIKit calling `LayoutSubviews()`.
- **Between `Program.Main` and the throw there is not one line of application code.**

Both paths unwind out through `UIApplication.Main`. The app cannot catch either, and unlike Android
there is no `Handled` flag to set (see the harness note under [Notes](#notes)).

So the platform thread-affinity check does not protect the collection. It only changes which
exception the app happens to see *first*, on the thread that made the off-thread call — while the
real corruption lands later, elsewhere, fatally.

**How reliably this reproduces.** Not from **B** alone: twenty-five consecutive **B** presses from a
fresh launch never terminated the process, every one producing nothing but the caught
`UIKitThreadAccessException`. Alternating **A** and **B**, five presses each, does terminate it.
That asymmetry is consistent with the mechanism — B's background loop dies on its *first* iteration
under UIKit's check, so each B press buys a single off-thread write, whereas **A**'s two loops run
unchecked and generate the sustained concurrent traffic that actually corrupts the set. The captured
report bears this out: the terminating thread-1 entry and an `A (custom BPs) THREW after 0.00s` entry
on thread 5 carry the same timestamp, and the layout frames name `Label.ArrangeOverride` inside the
`ContentView` that hosts the run's target — the same element **A** was writing to, from two threads,
in the same second.

**The AOT hypothesis in the original plan did not hold.** We expected the corruption might present
as `SIGSEGV`/`SIGABRT` with no managed stack under full AOT. It does not: every iOS failure —
including the uncaught one that terminates the process — produced a complete managed stack with a
normal `ToString()`, no different in kind from Android. Recorded here so the assumption doesn't
quietly persist into the issue text.

<details>
<summary>A/B — <code>IndexOutOfRangeException</code> on thread 1 in the <b>layout pass</b>, uncaught, terminates the process</summary>

Captured on a physical device, current harness build. Absolute repo paths elided and one generic
type argument collapsed to `[...]`; nothing else altered.

```
=== AppDomain.UnhandledException === 2026-08-14 18:26:17
Threw on thread: 1
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String value, Int32& location)
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(BindableProperty property, BindablePropertyContext context, Object value, Boolean currentlyApplying, SetValueFlags attributes, SetterSpecificity specificity, Boolean silent)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(BindableProperty property, Object value, SetValueFlags attributes, SetValuePrivateFlags privateAttributes, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindablePropertyKey propertyKey, Object value)
   at Microsoft.Maui.Controls.VisualElement.set_Width(Double value)
   at Microsoft.Maui.Controls.VisualElement.UpdateBoundsComponents(Rect bounds)
   at Microsoft.Maui.Controls.VisualElement.set_Frame(Rect value)
   at Microsoft.Maui.Controls.VisualElement.ArrangeOverride(Rect bounds)
   at Microsoft.Maui.Controls.Label.ArrangeOverride(Rect bounds)
   at Microsoft.Maui.Controls.VisualElement.Microsoft.Maui.IView.Arrange(Rect bounds)
   at Microsoft.Maui.Layouts.LayoutExtensions.ArrangeContent(IContentView contentView, Rect bounds)
   at Microsoft.Maui.Controls.TemplatedView.Microsoft.Maui.ICrossPlatformLayout.CrossPlatformArrange(Rect bounds)
   at Microsoft.Maui.Platform.MauiView.CrossPlatformArrange(CGRect bounds)
   at Microsoft.Maui.Platform.MauiView.LayoutSubviews()
   at Microsoft.Maui.Platform.ContentView.LayoutSubviews()
   at Microsoft.Maui.Platform.ContentView.__Registrar_Callbacks__.callback_447_Microsoft_Maui_Platform_ContentView_LayoutSubviews(IntPtr pobj, IntPtr sel, IntPtr* exception_gchandle)
--- End of stack trace from previous location ---
   at ObjCRuntime.Runtime.ThrowException(IntPtr gchandle) in /Users/cloudtest/vss/_work/1/s/macios/src/ObjCRuntime/Runtime.cs:line 2797
   at UIKit.UIApplication.UIApplicationMain(Int32 argc, String[] argv, IntPtr principalClassName, IntPtr delegateClassName) in /Users/cloudtest/vss/_work/1/s/macios/src/UIKit/UIApplication.cs:line 68
   at UIKit.UIApplication.Main(String[] args, Type principalClass, Type delegateClass) in /Users/cloudtest/vss/_work/1/s/macios/src/UIKit/UIApplication.cs:line 100
   at BindablePropertyConcurrencyRepro.Program.Main(String[] args) in Platforms/iOS/Program.cs:line 13
```

The next entry in the same report — same timestamp, same second — is the off-thread side of the same
race:

```
=== A (custom BPs) THREW after 0.00s === 2026-08-14 18:26:17
Threw on thread: 5
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String value, Int32& location)
   …
```
</details>

<details>
<summary>B — <code>InvalidOperationException</code> on thread 1 in the <b>animation ticker</b>, uncaught, terminates the process</summary>

Captured on a physical device on an earlier build of this harness (before per-run element
replacement and report timestamps), which is why there is no timestamp on the banner. The stack
itself is unaffected by those harness changes. File paths elided; nothing else altered.

```
=== AppDomain.UnhandledException ===
Threw on thread: 1
System.InvalidOperationException: Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.
   at System.Collections.Generic.HashSet`1[[System.String, ...]].AddIfNotPresent(String value, Int32& location)
   at Microsoft.Maui.Controls.Element.OnBindablePropertySet(BindableProperty property, Object original, Object value, Boolean changed, Boolean willFirePropertyChanged)
   at Microsoft.Maui.Controls.BindableObject.SetValueActual(BindableProperty property, BindablePropertyContext context, Object value, Boolean currentlyApplying, SetValueFlags attributes, SetterSpecificity specificity, Boolean silent)
   at Microsoft.Maui.Controls.BindableObject.SetValueCore(BindableProperty property, Object value, SetValueFlags attributes, SetValuePrivateFlags privateAttributes, SetterSpecificity specificity)
   at Microsoft.Maui.Controls.BindableObject.SetValue(BindableProperty property, Object value)
   at Microsoft.Maui.Controls.VisualElement.set_Opacity(Double value)
   at Microsoft.Maui.Controls.ViewExtensions.<>c.<FadeToAsync>b__3_0(VisualElement v, Double value)
   at Microsoft.Maui.Controls.ViewExtensions.<>c__DisplayClass1_0.<AnimateToAsync>g__UpdateProperty|0(Double f)
   at Microsoft.Maui.Controls.Animation.<>c__DisplayClass2_0.<.ctor>b__0(Double f)
   at Microsoft.Maui.Controls.Animation.<GetCallback>b__5_0(Double f)
   at Microsoft.Maui.Controls.AnimationExtensions.<>c__DisplayClass21_0`1[[System.Double, ...]].<AnimateInternal>b__0(Double f)
   at Microsoft.Maui.Controls.AnimationExtensions.HandleTweenerUpdated(Object o, EventArgs args)
   at Microsoft.Maui.Controls.Tweener.Step(Int64 step)
   at Microsoft.Maui.Controls.TweenerAnimation.OnTick(Double millisecondsSinceLastUpdate)
   at Microsoft.Maui.Animations.Animation.Tick(Double milliseconds)
   at Microsoft.Maui.Animations.AnimationManager.<OnFire>g__OnAnimationTick|20_0(Animation animation, <>c__DisplayClass20_0& )
   at Microsoft.Maui.Animations.AnimationManager.OnFire()
   at Microsoft.Maui.Animations.PlatformTicker.<Start>b__3_0()
   at Foundation.NSActionDispatcher.Apply()
   at Foundation.NSActionDispatcher.__Registrar_Callbacks__.callback_3687_Foundation_NSActionDispatcher_Apply(IntPtr pobj, IntPtr sel, IntPtr* exception_gchandle)
--- End of stack trace from previous location ---
   at ObjCRuntime.Runtime.ThrowException(IntPtr gchandle)
   at UIKit.UIApplication.UIApplicationMain(Int32 argc, String[] argv, IntPtr principalClassName, IntPtr delegateClassName)
   at UIKit.UIApplication.Main(String[] args, Type principalClass, Type delegateClass)
   at BindablePropertyConcurrencyRepro.Program.Main(String[] args)
```
</details>

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
them. This is exactly what UIKit's thread-affinity check masks, and how much: on *this* call, on the
background thread, execution never gets past `UILabel.set_TextColor` to reach
`Element.OnBindablePropertySet`/the `HashSet`, whereas on Android the same call reaches the set and
corrupts it. What the check does not do is prevent the corruption — the first trace in this section
is the same scenario killing the process from thread 1.)
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
- The throw sometimes lands on the UI thread inside framework code that no scenario can catch —
  either the animation ticker (`AnimationManager.OnFire` → … → `VisualElement.set_Opacity`) or the
  layout pass (`ContentView.LayoutSubviews` → … → `VisualElement.set_Width`). Neither is inside any
  app code. **In a real app this is a hard crash**, and the harness does not pretend otherwise — it
  only keeps itself usable where the platform allows:
  - **Android:** `AndroidEnvironment.UnhandledExceptionRaiser` exposes a `Handled` flag, so the
    report is captured, the exception is swallowed, and the run is reported as
    **THREW (unhandled)**. The app stays alive across repeated presses.
  - **iOS:** there is no equivalent — `ObjCRuntime.Runtime.MarshalManagedException` can change how
    an exception crosses into native code but cannot suppress it. The process is terminated. The
    report still survives via `last-crash.txt` and is shown on the next launch.
- `last-crash.txt` accumulates across runs; the on-screen report renders it **newest first**, with
  a timestamp per entry. **Clear report** empties it.
- `MauiXamlInflator` is left at the default (runtime/XamlC) rather than the template's `SourceGen`,
  to keep XAML codegen out of the variables under test.
