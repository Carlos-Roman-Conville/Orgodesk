# build-rc.ps1: builds this app's release candidate (RC) from a clean worktree of ONE commit, for OrgoAI v1.
# Rules (PRODUCTION.md principles 10 and 11): the RC comes from a committed state, never a working folder, and its name,
# size and SHA-256 are recorded. Nothing here releases anything: no tag, no GitHub release, no store or Gumroad change.
#
#   pwsh release\build-rc.ps1 -Commit <sha-or-ref>  [-BundleId <fake id>]  [-OutRoot <folder>]  [-LibCommit @{ 'crc-appshell' = '<sha>' }]  [-Keep]
#
# Output: <OutRoot>\<app>\<short commit>\  the installer (or exe/zip), rc.json (app, version, commit, library commits, file,
# size, SHA-256, time) and, while the bundle id is a placeholder, NOT-FOR-SALE.txt. Every such file's name says
# "NOT-FOR-SALE" because a Release build stops at CS0020 while OrgoSuite.BundleProductId is empty: the placeholder id is
# put in a THROWAWAY copy of the kit inside the worktree, on this command line only, and is never committed. A build with
# a placeholder id has no working bundle licence check against Gumroad: never give it to a buyer.
#
# Exit codes: 0 RC complete; 3 built but no installer exists at this commit (rc.json says so); anything else: failed.
# The config block below is the only part that differs between the eight repos; the rest is identical.
#requires -Version 7
param(
    [Parameter(Mandatory)][string]$Commit,
    [string]$BundleId = 'PLACEHOLDER-NOT-FOR-SALE',
    [string]$OutRoot = 'E:\AI Programs\_caches\tmp\v1-rc',
    [string]$SuiteRoot = 'E:\AI Programs\Toolchain\orgo-suite',   # where the libs\<name> git checkouts live (their commits are built too)
    [hashtable]$LibCommit = @{},
    [string]$Dotnet = 'E:\AI Programs\Toolchain\dotnet10\sdk\dotnet.exe',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'

# ---- per-repo config -------------------------------------------------------------------------------------------
$Cfg = @{
    App      = 'orgodesk'            # folder name, lower case
    Display  = 'Orgodesk'        # file name prefix
    RepoPath = 'apps\orgodesk'       # where the repo sits under the suite root (apps\orgodupe, or orgoai)
    Project  = 'src\Orgodesk.App'        # the exe project, relative to the repo
    Iss      = 'installer\Orgodesk.iss'            # Inno Setup script relative to the repo; '' = this repo ships no installer
    Kind     = 'installer'           # installer | exezip (an exe and its docs in a zip, for a repo with no installer) | exe
    Pre      = 'installer\make-legal-txt.ps1'            # a script the repo's own build-installer runs before publishing (Orgodesk: makes the legal text files); '' = none
    IssDefs  = '/DAppVersion={version}'        # extra Inno defines; {version} is replaced (Orgodesk's .iss takes /DAppVersion)
    PubExtra = '-p:DebugType=none'       # extra publish arguments, space separated (Orgodesk: -p:DebugType=none)
}
# ----------------------------------------------------------------------------------------------------------------

function G([string]$dir, [string[]]$a) { $o = & git -C $dir @a 2>&1; if ($LASTEXITCODE) { throw "git -C $dir $($a -join ' ') failed: $o" }; $o }
$repo = (G $PSScriptRoot @('rev-parse', '--show-toplevel')) | Select-Object -First 1
$repo = (Resolve-Path $repo).Path
$suite = $SuiteRoot

try { G $repo @('fetch', '--quiet', 'origin') | Out-Null } catch { Write-Warning "fetch failed (offline?): building from what is local" }
$sha = (G $repo @('rev-parse', '--verify', "$Commit^{commit}")) | Select-Object -First 1
$short = $sha.Substring(0, 9)
$out = Join-Path $OutRoot "$($Cfg.App)\$short"
$work = Join-Path $out 'work'
if (Test-Path $out) { throw "$out already exists: an RC folder is never overwritten (a rebuilt file is a new RC). Remove it yourself or use another commit." }
New-Item -ItemType Directory -Force $work | Out-Null

$trees = [System.Collections.Generic.List[object]]::new()
function AddTree([string]$gitDir, [string]$dest, [string]$rev) {
    G $gitDir @('worktree', 'add', '--detach', '--quiet', $dest, $rev) | Out-Null
    $trees.Add(@{ Git = $gitDir; Path = $dest })
}
$libShas = [ordered]@{}
$result = $null
try {
    $appTree = Join-Path $work $Cfg.RepoPath
    New-Item -ItemType Directory -Force (Split-Path $appTree -Parent) | Out-Null
    AddTree $repo $appTree $sha
    if ((G $appTree @('status', '--porcelain')).Count) { throw 'the worktree is not clean' }

    # Libraries the projects reference (libs\<name>, found by reading the project files, transitively), each a worktree beside the app.
    # Which suite libraries a tree names: any libs\<name> or ..\<name> path segment whose <name> is a folder in <suite>\libs.
    # OrgoAI itself (the suite's orgoai folder, a repo of its own) is also named by Orgodupe (Bit, OrgoAI.Companion): a sibling worktree at <work>\orgoai.
    $libNames = @(Get-ChildItem (Join-Path $suite 'libs') -Directory | ForEach-Object Name) + @('orgoai' | Where-Object { $_ -ne $Cfg.App })
    $queue = [System.Collections.Generic.Queue[string]]::new(); $seen = @{}
    $scan = { param($root) $texts = Get-ChildItem $root -Recurse -Include *.csproj, *.props, *.targets -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { Get-Content $_.FullName -Raw }
        foreach ($n in $libNames) { if ($texts | Where-Object { $_ -match ('[\\/]' + [regex]::Escape($n) + '(?![\w.-])') }) { $n } } }
    & $scan $appTree | ForEach-Object { if (-not $seen[$_]) { $seen[$_] = 1; $queue.Enqueue($_) } }
    while ($queue.Count) {
        $name = $queue.Dequeue(); $rel = if ($name -eq 'orgoai') { 'orgoai' } else { "libs\$name" }; $libGit = Join-Path $suite $rel
        if (-not (Test-Path (Join-Path $libGit '.git'))) { throw "$rel is not a git checkout under $suite" }
        try { G $libGit @('fetch', '--quiet', 'origin') | Out-Null } catch { }
        $rev = $LibCommit[$name]
        if (-not $rev) { $rev = foreach ($c in 'origin/HEAD', 'origin/master', 'origin/main') { try { G $libGit @('rev-parse', '--verify', "$c^{commit}") | Select-Object -First 1; break } catch { } } }
        $libSha = (G $libGit @('rev-parse', '--verify', "$rev^{commit}")) | Select-Object -First 1
        AddTree $libGit (Join-Path $work $rel) $libSha
        $libShas[$name] = $libSha
        & $scan (Join-Path $work $rel) | ForEach-Object { if (-not $seen[$_]) { $seen[$_] = 1; $queue.Enqueue($_) } }
    }

    # The placeholder bundle id: only in the throwaway kit copy, only when the committed id is empty.
    $placeholder = $false
    $sales = Join-Path $work 'libs\crc-appshell\src\Crc.AppShell\Commands\CommandSales.cs'
    if (Test-Path $sales) {
        $text = Get-Content $sales -Raw
        if ($text -match 'BundleProductId\s*=\s*"";') {
            Set-Content $sales ($text -replace 'BundleProductId\s*=\s*"";', "BundleProductId = `"$BundleId`";") -NoNewline
            $placeholder = $true
        }
    }
    $tag = if ($placeholder) { 'RC-NOT-FOR-SALE-placeholder-bundle-id' } else { 'RC' }

    # Version: the exe project's <Version>, else Directory.Build.props, else the installer script.
    $version = $null
    foreach ($f in @((Join-Path $appTree "$(($Cfg.Project -split ';')[0])\*.csproj"), (Join-Path $appTree 'Directory.Build.props'))) {
        $m = Get-ChildItem $f -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($m -and ((Get-Content $m.FullName -Raw) -match '<Version>([^<]+)</Version>')) { $version = $Matches[1]; break }
    }
    $issPath = if ($Cfg.Iss) { Join-Path $appTree $Cfg.Iss } else { $null }
    if (-not $version -and $issPath -and (Test-Path $issPath) -and ((Get-Content $issPath -Raw) -match '#define AppVersion "([^"]+)"')) { $version = $Matches[1] }
    if (-not $version) { $version = 'unknown' }

    # Build (the repo's own publish line).
    Push-Location $appTree
    try {
        $pubOut = if ($Cfg.Kind -eq 'exezip') { 'release' } else { 'release\publish' }
        if ($Cfg.Pre) { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $appTree $Cfg.Pre); if ($LASTEXITCODE) { throw "$($Cfg.Pre) failed (exit $LASTEXITCODE)" } }
        foreach ($proj in ($Cfg.Project -split ';')) {   # OrgoAI publishes two exes (the cockpit and the MCP server) into one folder
            $pub = @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true',
                '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', $pubOut, '-nologo', '-v', 'q')
            if ($Cfg.Kind -eq 'exezip') { $pub += '-p:DebugType=none' }
            if ($Cfg.PubExtra) { $pub += ($Cfg.PubExtra -split ' ') }
            & $Dotnet @pub
            if ($LASTEXITCODE) { throw "publish of $proj failed (exit $LASTEXITCODE)" }
        }
    } finally { Pop-Location }

    # The bare published exes beside the installer, for cold-start timing (a file's first run is the slow one: FC-66).
    $exeDir = Join-Path $out 'exe'; New-Item -ItemType Directory -Force $exeDir | Out-Null
    Get-ChildItem (Join-Path $appTree $pubOut) -Filter *.exe -File | Copy-Item -Destination $exeDir
    $complete = $true; $note = ''
    $files = @()
    if ($Cfg.Kind -eq 'installer' -and $issPath -and (Test-Path $issPath)) {
        $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) { throw 'Inno Setup 6 was not found' }
        $base = "$($Cfg.Display)-Setup-$version-$tag"
        $defs = @(); if ($Cfg.IssDefs) { $defs = @(($Cfg.IssDefs -replace '\{version\}', $version) -split ' ') }
        & $iscc '/Q' @defs "/O$out" "/F$base" $issPath
        if ($LASTEXITCODE) { throw "installer build failed (exit $LASTEXITCODE)" }
        $files += Join-Path $out "$base.exe"
    } elseif ($Cfg.Kind -eq 'exezip') {
        $stage = Join-Path $out 'stage'; New-Item -ItemType Directory -Force $stage | Out-Null
        Copy-Item (Join-Path $appTree 'release\*.exe') $stage
        foreach ($d in 'THIRD-PARTY-NOTICES.txt', 'CHANGELOG.md', 'README-buyers.txt') { if (Test-Path (Join-Path $appTree $d)) { Copy-Item (Join-Path $appTree $d) $stage } }
        if (Test-Path (Join-Path $appTree 'legal\EULA.md')) { Copy-Item (Join-Path $appTree 'legal\EULA.md') (Join-Path $stage 'EULA.txt') }
        $zip = Join-Path $out "$($Cfg.Display)-$version-$tag.zip"
        Compress-Archive -Path "$stage\*" -DestinationPath $zip
        Remove-Item $stage -Recurse -Force
        $files += $zip
    } else {
        # No installer in this commit: the published exe is kept so it can be tested, but the RC is not complete.
        $complete = $false
        $note = 'NO INSTALLER AT THIS COMMIT: only the published exe was built; this is not a releasable RC'
        $exe = Get-ChildItem (Join-Path $appTree 'release\publish\*.exe') | Select-Object -First 1
        $dest = Join-Path $out ("$($Cfg.Display)-$version-$tag-exe-only.exe"); Copy-Item $exe.FullName $dest
        $files += $dest
    }

    $entries = foreach ($f in $files) {
        $h = Get-FileHash $f -Algorithm SHA256
        [ordered]@{ file = Split-Path $f -Leaf; bytes = (Get-Item $f).Length; sha256 = $h.Hash }
    }
    $label = 'NOT FOR SALE: placeholder bundle id'
    $manifest = [ordered]@{
        app = $Cfg.App; version = $version; commit = $sha; repoBranchHint = (G $repo @('rev-parse', '--abbrev-ref', 'HEAD') | Select-Object -First 1)
        libs = $libShas; dotnetSdk = (& $Dotnet --version); builtUtc = (Get-Date).ToUniversalTime().ToString('o')
        complete = $complete; note = $note; placeholderBundleId = $placeholder
        label = $(if ($placeholder) { $label } else { '' }); files = @($entries)
        exes = @(Get-ChildItem $exeDir -File | ForEach-Object { [ordered]@{ file = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'rc.json')
    "$($entries | ForEach-Object { "$($_.sha256)  $($_.file)" })" | Set-Content (Join-Path $out 'SHA256SUMS.txt')
    if ($placeholder) {
        "$label`r`nBuilt $((Get-Date).ToString('s')) from $($Cfg.App) commit $sha. The bundle product id is a placeholder in a throwaway copy of the kit, so this build's AI-connection licence cannot be activated by a buyer. For testing only; never ship, upload or sell it.`r`n" | Set-Content (Join-Path $out 'NOT-FOR-SALE.txt')
    }
    $result = [pscustomobject]@{ App = $Cfg.App; Version = $version; Commit = $sha; Complete = $complete; Out = $out; Files = $entries }
} finally {
    if (-not $Keep) {
        foreach ($t in $trees) { try { & git -C $t.Git worktree remove --force $t.Path 2>&1 | Out-Null } catch { } }
        foreach ($g in ($trees | ForEach-Object { $_.Git } | Sort-Object -Unique)) { & git -C $g worktree prune 2>&1 | Out-Null }
        if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
$result | ConvertTo-Json -Depth 4
if (-not $result.Complete) { exit 3 }
