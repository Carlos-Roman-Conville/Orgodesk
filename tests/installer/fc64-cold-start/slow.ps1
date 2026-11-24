# FC-64: why does `Orgodesk.exe --orgo-command` status not answer on a first start? Guest side, Run box.
#   slow.ps1 -Step all     C:\orgo-tests\desk-slow\slow.log
param([string]$Step = 'all')
$ErrorActionPreference = 'Continue'
$dir = 'C:\orgo-tests\desk-slow'; $log = "$dir\slow.log"
function Say($t) { "{0:HH:mm:ss} {1}" -f (Get-Date), $t | Out-File -Append -Encoding utf8 $log }
function TempNet { $p = "$env:TEMP\.net"; if (Test-Path $p) { "{0} files {1} MB" -f (Get-ChildItem $p -Recurse -File -EA SilentlyContinue).Count, [int]((Get-ChildItem $p -Recurse -File -EA SilentlyContinue | Measure-Object Length -Sum).Sum / 1MB) } else { 'none' } }
function Ask([string]$label, [string]$exe, [int]$limit = 100, [switch]$KeepTrial) {
    if (-not $KeepTrial) { Remove-Item 'HKCU:\Software\Orgo\Premium' -Recurse -Force -EA SilentlyContinue; Remove-Item "$env:LOCALAPPDATA\Orgo\premium" -Recurse -Force -EA SilentlyContinue }
    $d = "$dir\data-$label-$(Get-Random)"; $in = "$dir\in.json"; $out = "$dir\out-$label.json"; $err = "$dir\err-$label.txt"
    Remove-Item $out, $err -EA SilentlyContinue
    [IO.File]::WriteAllText($in, '{"protocol":1,"verb":"status"}', (New-Object Text.UTF8Encoding $false))
    $t0 = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--orgo-command', '--data', "`"$d`"") -RedirectStandardInput $in -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
    $last = 0; $done = $false
    for ($i = 0; $i -lt ($limit / 2); $i++) {
        if ($p.WaitForExit(2000)) { $done = $true; break }
        $s = [int]((Get-Date) - $t0).TotalSeconds
        if ($s -ge $last + 8) {
            $last = $s; $p.Refresh()
            $mods = @($p.Modules | % ModuleName)
            $tcp = @(Get-NetTCPConnection -OwningProcess $p.Id -EA SilentlyContinue).Count
            Say ("   [{0}] +{1}s cpu={2:N1}s threads={3} handles={4} ws={5}MB modules={6} winforms={7} tcp={8} temp.net={9}" -f $label, $s, $p.TotalProcessorTime.TotalSeconds, $p.Threads.Count, $p.HandleCount, [int]($p.WorkingSet64 / 1MB), $mods.Count, [bool]($mods -match 'System.Windows.Forms'), $tcp, (TempNet))
        }
    }
    $secs = [Math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
    $o = if (Test-Path $out) { [IO.File]::ReadAllText($out) } else { '' }
    if (-not $done) { $p.Kill() }
    $ok = $o -match '"ok":\s*true'
    Say ("[{0}] finished={1} in {2} s; answered ok={3}; err='{4}'" -f $label, $done, $secs, $ok, ((Get-Content $err -Raw -EA SilentlyContinue) -replace '\s+', ' '))
}

Say "=== start ($Step); defender realtime: $((Get-MpComputerStatus -EA SilentlyContinue).RealTimeProtectionEnabled)"
Get-Process Orgodesk -EA SilentlyContinue | Stop-Process -Force
Remove-Item "$env:TEMP\.net" -Recurse -Force -EA SilentlyContinue
$zip = (Get-ChildItem $dir -Filter '*.zip' | Select-Object -First 1).FullName
$setup = (Get-ChildItem $dir -Filter '*Setup*.exe' | Select-Object -First 1).FullName
Say "zip: $zip ; setup: $setup"

# E1: the zip's exe, run from an ordinary folder. First start, then second, third.
Remove-Item "$dir\zipA" -Recurse -Force -EA SilentlyContinue; Expand-Archive $zip "$dir\zipA"
$exeA = "$dir\zipA\Orgodesk.exe"
Say "E1 zip exe, cold (first run of this file on this PC); temp.net before: $(TempNet)"
Ask 'zip-first' $exeA
Ask 'zip-second' $exeA
Ask 'zip-third-keeptrial' $exeA -KeepTrial

# E2: the zip layout the RC round used: unzipped into %LOCALAPPDATA%\Programs\Orgodesk, Defender scan first.
$inst = "$env:LOCALAPPDATA\Programs\Orgodesk"
Remove-Item $inst -Recurse -Force -EA SilentlyContinue; Expand-Archive $zip $inst
Remove-Item "$env:TEMP\.net" -Recurse -Force -EA SilentlyContinue
$sc = & "$env:ProgramFiles\Windows Defender\MpCmdRun.exe" -Scan -ScanType 3 -File "$inst\Orgodesk.exe" 2>&1 | Out-String
Say "E2 Defender scan: $(($sc -replace '\s+', ' ').Substring(0, [Math]::Min(100, $sc.Length)))"
Ask 'zip-installed-first' "$inst\Orgodesk.exe"
Ask 'zip-installed-second' "$inst\Orgodesk.exe"

# E3: the installer's exe (installed by the installer, which already ran it once with --autostart)
Remove-Item $inst -Recurse -Force -EA SilentlyContinue
Remove-Item "$env:TEMP\.net" -Recurse -Force -EA SilentlyContinue
$p = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=startup' -PassThru -Wait
Say "E3 installer exit $($p.ExitCode); temp.net after install: $(TempNet)"
Ask 'installer-first' "$inst\Orgodesk.exe"
'SLOW-DONE' | Out-File -Append -Encoding utf8 "$dir\slow.done"
