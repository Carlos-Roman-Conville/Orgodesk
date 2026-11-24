# Orgodesk installer VM round (FC-62), guest side. Runs in the tester's own session (started from the Run box).
#   inst.ps1 -Step all     every check below, in order, logged to C:\orgo-tests\desk-inst\inst.log (PASS / FAIL lines)
param([string]$Step = 'all')
$ErrorActionPreference = 'Continue'
$dir = 'C:\orgo-tests\desk-inst'
$log = "$dir\inst.log"
$setup = "$dir\Orgodesk-Setup-1.0.1-DEBUG.exe"
$app = "$env:LOCALAPPDATA\Programs\Orgodesk"
$data = "$env:APPDATA\orgodesk"
$guid = '{F3FD4A7E-5137-4FEA-BE79-9BE6ADF2C86C}'
$unkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${guid}_is1"
$legacykey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Orgodesk'
$runkey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startgroup = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Orgodesk"
$legacylnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Orgodesk.lnk"
$desktoplnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Orgodesk.lnk'
function Say($t) { "$(Get-Date -Format HH:mm:ss) $t" | Out-File -Append -Encoding utf8 $log }
function Chk([string]$name, [bool]$ok, [string]$why = '') { Say ("{0}  {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $why) }
function Running { @(Get-Process Orgodesk -ErrorAction SilentlyContinue).Count }
function RunVal { (Get-ItemProperty $runkey -Name Orgodesk -ErrorAction SilentlyContinue).Orgodesk }
function Titles { try { ((Get-Content "$data\layout.json" -Raw -Encoding UTF8 | ConvertFrom-Json).pages | % { $_.fences } | % { $_.title }) -join ',' } catch { "UNREADABLE" } }
function StartWith { try { (Get-Content "$data\settings.json" -Raw | ConvertFrom-Json).startWithWindows } catch { "UNREADABLE" } }
function Install([string]$tasks, [string]$tag) {
    $p = Start-Process $setup -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$dir\setup-$tag.log`" /TASKS=`"$tasks`"" -PassThru -Wait
    Say "installer ($tag, tasks '$tasks') exit $($p.ExitCode)"
    return $p.ExitCode
}
function Uninstall([string]$extra, [string]$tag) {
    $u = Get-ChildItem $app -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $u) { Say "no uninstaller in $app"; return -1 }
    $p = Start-Process $u.FullName -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$dir\unins-$tag.log`" $extra" -PassThru -Wait
    Say "uninstaller ($tag $extra) exit $($p.ExitCode)"
    Start-Sleep 4
    return $p.ExitCode
}

Say "=== start ($Step)"
if ($Step -eq 'ui') {   # leaves an installed copy with the seeded layout and the app running, for a visible uninstall
    Get-Process Orgodesk -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
    New-Item -ItemType Directory -Force "$data\snapshots" | Out-Null
    Copy-Item "$dir\seed\layout.json" "$data\layout.json" -Force; Copy-Item "$dir\seed\settings.json" "$data\settings.json" -Force
    Copy-Item "$dir\seed\Monday.json" "$data\snapshots\Monday.json" -Force
    Install 'startup' 'ui' | Out-Null
    Start-Process "$app\Orgodesk.exe"; Start-Sleep 15
    Say "ui ready: running $(Running); groups [$(Titles)]"; return
}
if ($Step -eq 'after') {
    Say "app folder exists: $(Test-Path $app); contents: $((Get-ChildItem $app -Recurse -ErrorAction SilentlyContinue | % FullName) -join '; ')"
    Say "data: $((Get-ChildItem $data -ErrorAction SilentlyContinue | % Name) -join ',') ; groups [$(Titles)] ; snapshot kept: $(Test-Path "$data\snapshots\Monday.json")"
    Say "Run [$(RunVal)] ; startWithWindows [$(StartWith)] ; procs $(Running) ; uninstall entry $(Test-Path $unkey) ; trial key $(Test-Path 'HKCU:\Software\Orgodesk')"
    return
}
# ---- 0. what the clean snapshot has
Get-Process Orgodesk -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
New-Item -ItemType Directory -Force "$data\snapshots" | Out-Null
Copy-Item "$dir\seed\layout.json" "$data\layout.json" -Force; Copy-Item "$dir\seed\settings.json" "$data\settings.json" -Force
Copy-Item "$dir\seed\Monday.json" "$data\snapshots\Monday.json" -Force
$snapHash = (Get-FileHash "$data\snapshots\Monday.json").Hash
if (-not (Test-Path $legacykey)) {   # the exe's own --install leaves these; fake them if the image has none
    New-Item $legacykey -Force | Out-Null; Set-ItemProperty $legacykey UninstallString "`"$app\Orgodesk.exe`" --uninstall"
    Set-Content -Path $legacylnk -Value 'x'; Say "legacy self-install entries faked"
} else { Say "legacy self-install entry present in the image" }
$before = Titles
Say "baseline: app folder exists $(Test-Path $app); data layout titles [$before]; startWithWindows [$(StartWith)]; Run [$(RunVal)]"
Copy-Item "$data" "$dir\data-before" -Recurse -Force -ErrorAction SilentlyContinue

# ---- 1. install over the legacy self-install, startup + desktop shortcut chosen
$e = Install 'startup,desktopicon' 'a'
Chk 'install exits 0' ($e -eq 0)
Chk 'program files: exe' (Test-Path "$app\Orgodesk.exe")
Chk 'legal folder holds the 4 texts' ((@('EULA.txt', 'PRIVACY.txt', 'REFUNDS.txt', 'THIRD-PARTY-NOTICES.txt') | ? { -not (Test-Path "$app\legal\$_") }).Count -eq 0)
Chk 'licence text is readable (no markdown marks)' ((Get-Content "$app\legal\EULA.txt" -Raw) -notmatch '(?m)^#|\*\*')
Chk 'Start menu shortcut in the Orgodesk group' (Test-Path "$startgroup\Orgodesk.lnk")
Chk 'desktop shortcut (chosen)' (Test-Path $desktoplnk)
Chk 'uninstall entry (Inno)' (Test-Path $unkey)
Chk 'legacy self-install uninstall entry replaced' (-not (Test-Path $legacykey))
Chk 'legacy Start menu shortcut removed' (-not (Test-Path $legacylnk))
Chk 'start with Windows: Run key points at the installed exe' ((RunVal) -eq "`"$app\Orgodesk.exe`"") "[$(RunVal)]"
Chk 'start with Windows: settings.json agrees' ((StartWith) -eq $true) "[$(StartWith)]"
Chk 'the person''s layout untouched by install' ((Titles) -eq $before -and $before -like 'Projects,Games*') "[$(Titles)] vs [$before]"
Chk 'app not started by a silent install' ((Running) -eq 0)

# ---- 2. upgrade while the app runs, startup now declined
Start-Process "$app\Orgodesk.exe"; Start-Sleep 18
Chk 'app running before the upgrade' ((Running) -ge 1)
$e = Install '!startup' 'b'
Chk 'upgrade over the running app exits 0' ($e -eq 0)
Chk 'running app was asked to leave' ((Running) -eq 0) "($(Running) left)"
Chk 'start with Windows off: Run key gone' (-not (RunVal)) "[$(RunVal)]"
Chk 'start with Windows off: settings.json agrees' ((StartWith) -eq $false) "[$(StartWith)]"
Chk 'one uninstall entry only' ((Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | ? { $_.PSChildName -like 'Orgodesk*' -or $_.PSChildName -like '*F3FD4A7E*' }).Count -eq 1)
Chk 'no flag left saying the real icons are hidden' (-not (Test-Path "$data\icons-hidden.flag"))
$trialKey = Test-Path 'HKCU:\Software\Orgodesk'
Say "trial key HKCU\Software\Orgodesk exists: $trialKey"
$titlesBeforeUninstall = Titles

# ---- 3. uninstall while the app runs: layouts kept
Start-Process "$app\Orgodesk.exe"; Start-Sleep 18
Chk 'app running before the uninstall' ((Running) -ge 1)
$e = Uninstall '' 'keep'
Chk 'uninstall exits 0' ($e -eq 0)
Chk 'app stopped' ((Running) -eq 0)
Chk 'program folder gone' (-not (Test-Path "$app\Orgodesk.exe")) "(left: $((Get-ChildItem $app -ErrorAction SilentlyContinue | % Name) -join ','))"
Chk 'Start menu group gone' (-not (Test-Path "$startgroup\Orgodesk.lnk"))
Chk 'desktop shortcut gone' (-not (Test-Path $desktoplnk))
Chk 'uninstall entry gone' (-not (Test-Path $unkey))
Chk 'Run key gone' (-not (RunVal))
Chk 'no flag left saying the real icons are hidden' (-not (Test-Path "$data\icons-hidden.flag"))
Chk 'layouts kept: data folder and layout.json' ((Test-Path "$data\layout.json"))
Chk 'layouts kept: same groups' ((Titles) -eq $titlesBeforeUninstall -and $titlesBeforeUninstall -like 'Projects,Games*') "[$(Titles)] vs [$titlesBeforeUninstall]"
Chk 'layouts kept: snapshot file byte for byte' ((Get-FileHash "$data\snapshots\Monday.json").Hash -eq $snapHash)
Chk 'start with Windows stays off in settings' ((StartWith) -eq $false) "[$(StartWith)]"
Chk 'trial key kept' ((Test-Path 'HKCU:\Software\Orgodesk') -eq $trialKey)

# ---- 4. reinstall picks the layouts up, then uninstall asking to delete them
$e = Install 'startup' 'c'
Chk 'reinstall exits 0' ($e -eq 0)
Chk 'reinstall: layouts there' ((Titles) -eq $titlesBeforeUninstall -and (Get-FileHash "$data\snapshots\Monday.json").Hash -eq $snapHash) "[$(Titles)]"
Chk 'reinstall: start with Windows on again' ((RunVal) -eq "`"$app\Orgodesk.exe`"" -and (StartWith) -eq $true)
$e = Uninstall '/DELETEDATA=1' 'delete'
Chk 'uninstall with delete: exits 0' ($e -eq 0)
Chk 'data folder deleted when asked' (-not (Test-Path $data)) "(left: $((Get-ChildItem $data -ErrorAction SilentlyContinue | % Name) -join ','))"
Chk 'trial key still kept when data is deleted' ((Test-Path 'HKCU:\Software\Orgodesk') -eq $trialKey)
Chk 'Run key gone' (-not (RunVal))
Say "=== done"
'INST-DONE' | Out-File -Append -Encoding utf8 "$dir\inst.done"
