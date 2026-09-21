# iOS builds driven from Windows strip parameter names (missing `KeepMetadata`)

## Goal
Show that when an iOS app is built from Windows (Pair to Mac), ILLink removes method/constructor
parameter names from trimmed assemblies, even in Debug. The same Debug build driven from the .NET SDK's
own `_RunILLink` target passes `--keep-metadata all`, because `DebuggerSupport` is not `false`.
The `Microsoft.iOS.Windows.Sdk` pack overrides `_RunILLink` with `Xamarin.MacDev.Tasks.ILLink` and does not
forward `KeepMetadata` (or `PreserveSymbolPaths`).

Observable effect: `System.Text.Json`'s reflection serializer throws for any trimmed type with a
parameterized constructor:

```
System.NotSupportedException: The deserialization constructor for type 'MQTTnet.MqttClientPublishResult'
contains parameters with null names. This might happen because the parameter names have been trimmed by ILLink.
```

## Environment
- Windows 11, .NET SDK `11.0.100-preview.7.26381.103` ([net11/global.json](net11/global.json))
- `Microsoft.iOS.Sdk` / `Microsoft.iOS.Windows.Sdk` `26.5.11997-net11-p7` (CoreCLR, `UseInterpreter=false`)
- Also reproduces on .NET SDK `10.0.303` with `Microsoft.iOS.Windows.Sdk` `26.5.10315` (Mono, `MtouchInterpreter=all`)
  ([net10/](net10/)). The omission is present in every installed Windows pack from net8.0 through net11.0 and on
  `dotnet/macios` `main`.
- Remote Mac paired through Visual Studio. The command line reuses that pairing via `-p:ServerAddress` / `-p:ServerUser`.
- Trimmed assembly under test: `MQTTnet` (`IsTrimmable=true`), 5.2.0.1603 (net11) / 5.1.0.1559 (net10).

## What the projects do
Plain (non-MAUI) iOS app. [Main.cs](net11/Main.cs) has a `TrimProbe.Touch()` that constructs
`MqttClientPublishResult`, serializes it with `JsonSerializer.Serialize`, and calls
`MqttClientOptionsBuilder.WithCleanSession(true)`. `AppDelegate.FinishedLaunching` calls it and writes
`TrimProbe: OK ...` or `TrimProbe: FAILED: ...` to the console.

## Repro steps (metadata inspection, no device needed)
From `net11/`:

```powershell
dotnet build TrimTest11.csproj -c Debug -p:RuntimeIdentifier=ios-arm64 -p:ServerAddress=<mac-ip> -p:ServerUser=<mac-user>
pwsh -NoProfile -File ..\inspect.ps1 -Path obj\Debug\net11.0-ios\ios-arm64\linked\MQTTnet.dll
```

For `net10/` add `-p:_CopyLinkerOutputToWindows=true` (under Mono the Windows pack does not copy linker output back
by default) and inspect `obj\Debug\net10.0-ios\ios-arm64\linked\MQTTnet.dll`.

[inspect.ps1](inspect.ps1) reads the Param table with `System.Reflection.Metadata` and prints the parameter names of
`MqttClientPublishResult..ctor` and `MqttClientOptionsBuilder.WithCleanSession`.
(It also prints a harmless error from a `GetTableRowCount` call; ignore it.)

## Expected
Debug build, `DebuggerSupport` not disabled, so the SDK default `--keep-metadata all` applies:

```
    .ctor paramRows=4 ['packetIdentifier','reasonCode','reasonString','userProperties']
    WithCleanSession paramRows=1 ['value']
```

## Actual
```
FILE: ...\linked\MQTTnet.dll  size=11776  types=...
  TYPE MQTTnet.MqttClientPublishResult
    .ctor paramRows=0 []
  TYPE MQTTnet.MqttClientOptionsBuilder
    WithCleanSession paramRows=1 ['']
```

Identical result on net10 (Mono) and net11 (CoreCLR). The illink argument list logged by the build has no
`--keep-metadata` at all:

```
-a "TrimTest11" EntryPoint
--trim-mode link
-reference ".../mqttnet/5.2.0.1603/lib/net10.0/MQTTnet.dll"
...
--feature System.Diagnostics.Debugger.IsSupported true
--feature ObjCRuntime.Runtime.IsCoreCLR true
--custom-step ... (macios dotnet-linker steps)
--custom-data "LinkerOptionsFile=..." --verbose -b --disable-opt unusedtypechecks --enable-serialization-discovery ...
```

## Runtime effect
Deploy the Debug build to a device and watch the console. Expected `TrimProbe: OK ...`; with the stripped names it
prints `TrimProbe: FAILED: System.NotSupportedException: The deserialization constructor for type
'MQTTnet.MqttClientPublishResult' contains parameters with null names ...`.

## Cause
`Microsoft.iOS.Windows.Sdk/<ver>/tools/msbuild/iOS/Xamarin.iOS.Common.After.targets` target `_RunILLink` invokes
`Xamarin.MacDev.Tasks.ILLink` without `KeepMetadata="@(_TrimmerKeepMetadata)"` and without
`PreserveSymbolPaths="$(_TrimmerPreserveSymbolPaths)"`. The .NET SDK's `Microsoft.NET.ILLink.targets` passes both.
`_TrimmerKeepMetadata` is `all` whenever `DebuggerSupport != false`.

Same on `dotnet/macios` `main`:
https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.iOS.Tasks.Windows/Xamarin.iOS.Common.After.targets
(`_RunILLink`, `Xamarin.MacDev.Tasks.ILLink` element).

## Why MAUI apps only see this on .NET 11
MAUI's `Microsoft.Maui.Controls.iOS.targets` sets `MtouchLink=None` for Debug when `UseInterpreter=true`, so Debug
device builds on Mono were never trimmed. Under CoreCLR (`UseInterpreter=false`, and MAUI 11 adds
`UseMonoRuntime=='true'` to that rule) Debug builds are trimmed, and the missing `KeepMetadata` becomes visible.
This non-MAUI repro trims in Debug on both runtimes, so it shows the underlying problem is runtime-independent.

## Workaround
Pass the flag the SDK would have passed:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <_ExtraTrimmerArgs>$(_ExtraTrimmerArgs) --keep-metadata all</_ExtraTrimmerArgs>
</PropertyGroup>
```

The Windows pack forwards `_ExtraTrimmerArgs` to the remote ILLink task as `ExtraArgs`. The flag itself is verified to be
the only difference (see below). Alternatively `<MtouchLink>None</MtouchLink>` avoids trimming altogether.

## Mac-free confirmation that `--keep-metadata` is the differentiator
[local-illink.ps1](local-illink.ps1) runs the same `illink.dll` on Windows against the compiled `TrimTest11.dll`, the
MQTTnet package assembly, `Microsoft.iOS.dll`, and the CoreCLR iOS runtime pack, once without and once with
`--keep-metadata all`. Everything else is identical:

```
===== illink without --keep-metadata all
FILE: .../local-illink-without/MQTTnet.dll  size=12288  types=14  methods=33
  TYPE MQTTnet.MqttClientOptionsBuilder
    WithCleanSession paramRows=1 ['']
  TYPE MQTTnet.MqttClientPublishResult
    .ctor paramRows=0 []
===== illink with --keep-metadata all
FILE: .../local-illink-with/MQTTnet.dll  size=12800  types=14  methods=33
  TYPE MQTTnet.MqttClientOptionsBuilder
    WithCleanSession paramRows=1 ['value']
  TYPE MQTTnet.MqttClientPublishResult
    .ctor paramRows=4 ['packetIdentifier','reasonCode','reasonString','userProperties']
```

## Notes
- The paired builds end in an unrelated native step failure on this Mac (`net10`: clang `'xamarin/xamarin.h' file not found`;
  `net11`: `install_name_tool` usage error). Linking has already completed by then and `obj/.../linked/` is populated,
  so the inspection is unaffected.
