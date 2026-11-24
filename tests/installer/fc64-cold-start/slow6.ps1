# FC-64: cold-start cost of publish variants (compressed vs not). Each exe is a never-seen file (its own hash), run from its own folder.
param([string]$Step = 'all')
$ErrorActionPreference = 'Continue'
$dir = 'C:\orgo-tests\desk-slow'; $log = "$dir\slow6.log"
function Say($t) { "{0:HH:mm:ss} {1}" -f (Get-Date), $t | Out-File -Append -Encoding utf8 $log }
function Ask([string]$label, [string]$exe) {
    $d = "$dir\data-$label"; $in = "$dir\in.json"; $out = "$dir\out-$label.json"
    $env:ORGODESK_CMD_TRACE = "$dir\trace-$label.log"; Remove-Item $env:ORGODESK_CMD_TRACE, $out -EA SilentlyContinue
    [IO.File]::WriteAllText($in, '{"protocol":1,"verb":"status"}', (New-Object Text.UTF8Encoding $false))
    $t0 = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--orgo-command', '--data', "`"$d`"") -RedirectStandardInput $in -RedirectStandardOutput $out -PassThru -WindowStyle Hidden
    $done = $p.WaitForExit(120000); $total = ((Get-Date) - $t0).TotalSeconds
    if (-not $done) { $p.Kill() }
    $pre = 'n/a'
    if (Test-Path $env:ORGODESK_CMD_TRACE) {
        $m = Get-Content $env:ORGODESK_CMD_TRACE | Select-String ' main$' | Select-Object -First 1
        if ($m) { $main = [datetime]::ParseExact(($m.Line -split ' ')[0], 'HH:mm:ss.fff', $null); $pre = [Math]::Round(($main.TimeOfDay - $t0.TimeOfDay).TotalSeconds, 2) }
    }
    $ok = (Test-Path $out) -and ((Get-Content $out -Raw) -match '"ok":\s*true')
    Say ("[{0}] total {1:N1} s; before Main {2} s; answered ok={3}" -f $label, $total, $pre, $ok)
}
Say "=== start; uptime $([int]((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalSeconds) s; share visible: $(Test-Path '\\VBoxSvr\fc64\var-C1\Orgodesk.exe')"
foreach ($n in 'C1', 'P1', 'C2', 'P2', 'C3', 'P3', 'C4', 'P4') {
    Remove-Item "$dir\$n" -Recurse -Force -EA SilentlyContinue; New-Item -ItemType Directory -Force "$dir\$n" | Out-Null
    Copy-Item "\\VBoxSvr\fc64\var-$n\Orgodesk.exe" "$dir\$n\Orgodesk.exe"      # copied from the host share, not measured
    Ask $n "$dir\$n\Orgodesk.exe"
    Ask "$n-again" "$dir\$n\Orgodesk.exe"
    Ask "$n-third" "$dir\$n\Orgodesk.exe"
}
'SLOW-DONE' | Out-File -Append -Encoding utf8 "$dir\slow6.done"
