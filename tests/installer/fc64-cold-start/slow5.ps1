# FC-64: cold-start cost of publish variants. Each variant's exe is a never-seen file (its own hash), run once from its own folder.
param([string]$Step = 'all')
$ErrorActionPreference = 'Continue'
$dir = 'C:\orgo-tests\desk-slow'; $log = "$dir\slow5.log"
function Say($t) { "{0:HH:mm:ss} {1}" -f (Get-Date), $t | Out-File -Append -Encoding utf8 $log }
function Ask([string]$label, [string]$exe) {
    $d = "$dir\data-$label"; $in = "$dir\in.json"; $out = "$dir\out-$label.json"
    $env:ORGODESK_CMD_TRACE = "$dir\trace-$label.log"; Remove-Item $env:ORGODESK_CMD_TRACE, $out -EA SilentlyContinue
    [IO.File]::WriteAllText($in, '{"protocol":1,"verb":"status"}', (New-Object Text.UTF8Encoding $false))
    $net0 = if (Test-Path "$env:TEMP\.net") { (Get-ChildItem "$env:TEMP\.net" -Recurse -File).Count } else { 0 }
    $t0 = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--orgo-command', '--data', "`"$d`"") -RedirectStandardInput $in -RedirectStandardOutput $out -PassThru -WindowStyle Hidden
    $done = $p.WaitForExit(120000); $total = ((Get-Date) - $t0).TotalSeconds
    if (-not $done) { $p.Kill() }
    $main = $null
    if (Test-Path $env:ORGODESK_CMD_TRACE) { $m = (Get-Content $env:ORGODESK_CMD_TRACE | Select-String ' main$' | Select-Object -First 1); if ($m) { $main = [datetime]::ParseExact(($m.Line -split ' ')[0], 'HH:mm:ss.fff', $null) } }
    $pre = if ($main) { [Math]::Round(($main.TimeOfDay - $t0.TimeOfDay).TotalSeconds, 2) } else { 'n/a' }
    $net1 = if (Test-Path "$env:TEMP\.net") { (Get-ChildItem "$env:TEMP\.net" -Recurse -File).Count } else { 0 }
    $ok = (Test-Path $out) -and ((Get-Content $out -Raw) -match '"ok":\s*true')
    Say ("[{0}] total {1:N1} s; before Main {2} s; answered ok={3}; files extracted to temp .net: {4}" -f $label, $total, $pre, $ok, ($net1 - $net0))
}
Say "=== start; uptime $([int]((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalSeconds) s"
foreach ($n in 'B1', 'A1', 'B2', 'A2') {
    Remove-Item "$dir\$n" -Recurse -Force -EA SilentlyContinue; Expand-Archive "$dir\var-$n.zip" "$dir\$n"
    Ask $n "$dir\$n\Orgodesk.exe"
    Ask "$n-again" "$dir\$n\Orgodesk.exe"
}
# does the no-extract variant start its window without the extra natives? (B1 folder holds the exe only)
$w = Start-Process "$dir\B1\Orgodesk.exe" -ArgumentList '--data', "$dir\win-B1" -PassThru; Start-Sleep 15
Say "B1 window process alive after 15 s: $(-not $w.HasExited)"
Get-Process Orgodesk -EA SilentlyContinue | Stop-Process -Force
'SLOW-DONE' | Out-File -Append -Encoding utf8 "$dir\slow5.done"
