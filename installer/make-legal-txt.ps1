<#
.SYNOPSIS
  Makes the installer's plain-text legal files from the owner's markdown in legal\ (EULA.md, PRIVACY.md, REFUNDS.md),
  into release\legal-txt\. The markdown is never changed. The Inno wizard shows text, not markdown, and wraps words
  itself, so: heading marks, bold and italic marks and link addresses go, a table row becomes a list line, and each
  paragraph goes on one line (crc-appshell's reflow-legal.ps1, kit-1.5). THIRD-PARTY-NOTICES.txt is copied as it is.
.EXAMPLE
  powershell -File installer\make-legal-txt.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'release\legal-txt'
New-Item -ItemType Directory -Force $out | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

foreach ($name in 'EULA', 'PRIVACY', 'REFUNDS') {
    $lines = [System.IO.File]::ReadAllText((Join-Path $repo "legal\$name.md")).Replace("`r`n", "`n").Split("`n")
    $text = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lines) {
        if ($l -match '^\|[\s:|-]+\|\s*$') { continue }                     # a table's separator row
        if ($l -match '^\|') {                                                # a table row: one list line
            $cells = ($l.Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() }
            $l = '- ' + ($cells -join ' / ')
        }
        $l = $l -replace '^#{1,6}\s+', ''                                      # heading marks
        $l = $l -replace '\[([^\]]+)\]\((https?://[^)]+)\)', '$1 ($2)'        # links keep their address in words
        $l = $l -replace '\*\*([^*]+)\*\*', '$1' -replace '(?<![*\w])\*([^*]+)\*(?![*\w])', '$1' -replace '`', ''
        $text.Add($l)
    }
    [System.IO.File]::WriteAllText((Join-Path $out "$name.txt"), ($text -join "`r`n"), $utf8)
}
& (Join-Path $PSScriptRoot '..\..\..\libs\crc-appshell\templates\reflow-legal.ps1') (Join-Path $out 'EULA.txt') (Join-Path $out 'PRIVACY.txt') (Join-Path $out 'REFUNDS.txt') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.txt') (Join-Path $out 'THIRD-PARTY-NOTICES.txt') -Force
Get-ChildItem $out | ForEach-Object { "{0}  {1} bytes" -f $_.Name, $_.Length }
