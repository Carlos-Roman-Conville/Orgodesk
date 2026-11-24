# FC-64 proof: ONE cold first start of the final exe on a freshly restored VM. Copies it from the host share, runs `status` once.
$dir = 'C:\orgo-tests\desk-slow'; New-Item -ItemType Directory -Force "$dir\F" | Out-Null
Copy-Item '\\VBoxSvr\fc64\var-F\Orgodesk.exe' "$dir\F\Orgodesk.exe"
$exe = "$dir\F\Orgodesk.exe"; $in = "$dir\in.json"; $out = "$dir\out-F.json"; $d = "$dir\data-F"
$env:ORGODESK_CMD_TRACE = "$dir\trace-F.log"; Remove-Item $env:ORGODESK_CMD_TRACE, $out -EA SilentlyContinue
[IO.File]::WriteAllText($in, '{"protocol":1,"verb":"status"}', (New-Object Text.UTF8Encoding $false))
$up = [int]((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalSeconds
$t0 = Get-Date
$p = Start-Process -FilePath $exe -ArgumentList @('--orgo-command', '--data', "`"$d`"") -RedirectStandardInput $in -RedirectStandardOutput $out -PassThru -WindowStyle Hidden
$done = $p.WaitForExit(120000); $total = ((Get-Date) - $t0).TotalSeconds
if (-not $done) { $p.Kill() }
$j = if (Test-Path $out) { Get-Content $out -Raw } else { '' }
$ok = $j -match '"ok":\s*true'; $access = if ($j -match '"access":\s*"([^"]+)"') { $Matches[1] } else { '?' }
"COLD uptime=${up}s total={0:N1}s finished=$done ok=$ok access=$access exeMB={1:N0}" -f $total, ((Get-Item $exe).Length / 1MB)
