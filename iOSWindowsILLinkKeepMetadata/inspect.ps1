param([string]$Path, [string]$TypeFilter = "MqttClientPublishResult|MqttClientOptionsBuilder")
Add-Type -AssemblyName System.Reflection.Metadata
$fs = [System.IO.File]::OpenRead($Path)
$pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
$md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
"FILE: $Path  size=$((Get-Item $Path).Length)  types=$($md.TypeDefinitions.Count)  methods=$($md.MethodDefinitions.Count)  params=$($md.GetTableRowCount([System.Reflection.Metadata.Ecma335.TableIndex]::Param))"
foreach ($th in $md.TypeDefinitions) {
  $t = $md.GetTypeDefinition($th); $tn = $md.GetString($t.Name)
  if ($tn -notmatch $TypeFilter) { continue }
  "  TYPE $($md.GetString($t.Namespace)).$tn"
  foreach ($mh in $t.GetMethods()) {
    $m = $md.GetMethodDefinition($mh); $mn = $md.GetString($m.Name)
    if ($mn -ne ".ctor" -and $mn -notmatch "WithCleanSession|Build$") { continue }
    $names = @(); foreach ($ph in $m.GetParameters()) { $p = $md.GetParameter($ph); $names += "'" + $md.GetString($p.Name) + "'" }
    "    $mn paramRows=$($names.Count) [$($names -join ',')]"
  }
}
$pe.Dispose(); $fs.Dispose()
