<#
.SYNOPSIS
    Raphael preflight check — verifies the working tree is in a shippable state.

.DESCRIPTION
    Runs from the Raphael repo root. Two modes:

      -Mode Commit   (default)  : Fast checks suitable for a pre-commit hook.
      -Mode Release             : Full checks before packaging for Thunderstore.

    Exit code 0 = all good. Non-zero = at least one check failed.
    See docs/PREFLIGHT.md for the full rationale of each check.

.EXAMPLE
    .\tools\preflight.ps1
    .\tools\preflight.ps1 -Mode Release
#>

[CmdletBinding()]
param(
    [ValidateSet('Commit', 'Release')]
    [string]$Mode = 'Commit'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$fails = New-Object System.Collections.Generic.List[string]
$warns = New-Object System.Collections.Generic.List[string]

function Fail([string]$msg) { $fails.Add($msg) | Out-Null; Write-Host "  FAIL  $msg" -ForegroundColor Red }
function Warn([string]$msg) { $warns.Add($msg) | Out-Null; Write-Host "  WARN  $msg" -ForegroundColor Yellow }
function Pass([string]$msg) {                                 Write-Host "  OK    $msg" -ForegroundColor Green }

Write-Host "Raphael preflight ($Mode mode)" -ForegroundColor Cyan
Write-Host ('-' * 60)

# ---- 1. Version sync: csproj <Version> == thunderstore.toml versionNumber ----
$csproj = Join-Path $repoRoot 'Raphael\Raphael.csproj'
$toml   = Join-Path $repoRoot 'Raphael\thunderstore.toml'

if (-not (Test-Path $csproj)) { Fail "Missing $csproj"; }
if (-not (Test-Path $toml))   { Fail "Missing $toml"; }

$csprojVersion = $null
$tomlVersion   = $null
if (Test-Path $csproj) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
    if ($m.Success) { $csprojVersion = $m.Groups[1].Value } else { Fail "Could not parse <Version> from $csproj" }
}
if (Test-Path $toml) {
    $m = [regex]::Match((Get-Content $toml -Raw), 'versionNumber\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"')
    if ($m.Success) { $tomlVersion = $m.Groups[1].Value } else { Fail "Could not parse versionNumber from $toml" }
}
if ($csprojVersion -and $tomlVersion) {
    if ($csprojVersion -eq $tomlVersion) {
        Pass "Version sync: csproj=$csprojVersion, thunderstore.toml=$tomlVersion"
    } else {
        Fail "Version drift: csproj=$csprojVersion != thunderstore.toml=$tomlVersion"
    }
}

# ---- 2. IS_TESTING must be false in Plugin.cs ----
$pluginCs = Join-Path $repoRoot 'Raphael\Plugin.cs'
if (Test-Path $pluginCs) {
    $content = Get-Content $pluginCs -Raw
    if ($content -match 'IS_TESTING\s*=\s*true') {
        Fail "Plugin.IS_TESTING is true — flip to false before commit/release."
    } else {
        Pass "Plugin.IS_TESTING is false."
    }
}

# ---- 3. Resources/secrets.json must NOT contain a real key ----
$secrets = Join-Path $repoRoot 'Raphael\Resources\secrets.json'
if (Test-Path $secrets) {
    try {
        $json = Get-Content $secrets -Raw | ConvertFrom-Json
        $key = [string]$json.newSharedKey
        if ([string]::IsNullOrEmpty($key)) {
            Pass "secrets.json holds an empty placeholder key."
        } else {
            # Decode base64 length; ≥16 raw bytes is "looks real".
            try {
                $bytes = [Convert]::FromBase64String($key)
                if ($bytes.Length -ge 16) {
                    Fail "secrets.json contains a real-looking key ($($bytes.Length) bytes). Move it to Resources/secrets.local.json (gitignored)."
                } else {
                    Warn "secrets.json key is non-empty but short ($($bytes.Length) bytes) — leave empty unless intentional."
                }
            } catch {
                Warn "secrets.json key is non-empty and not valid base64. Leave empty in committed file."
            }
        }
    } catch {
        Fail "secrets.json is not valid JSON: $($_.Exception.Message)"
    }
}

# ---- 4. No merge-conflict markers in tracked files ----
$tracked = & git ls-files 2>$null
if ($LASTEXITCODE -eq 0 -and $tracked) {
    $conflictHits = $tracked | Where-Object {
        $p = Join-Path $repoRoot $_
        if (-not (Test-Path $p -PathType Leaf)) { return $false }
        if ($p -match '\\(tools|docs|\.githooks)\\') { return $false } # docs/scripts mention these legitimately
        $sample = Get-Content $p -TotalCount 200 -ErrorAction SilentlyContinue
        ($sample -match '^<{7} ') -or ($sample -match '^={7}$') -or ($sample -match '^>{7} ')
    }
    if ($conflictHits) {
        foreach ($f in $conflictHits) { Fail "Merge-conflict markers in $f" }
    } else {
        Pass "No merge-conflict markers in tracked files."
    }
}

# ---- 5. Stray Console.WriteLine / UnityEngine.Debug.Log in .cs (warn only) ----
$strayLogs = Get-ChildItem -Path (Join-Path $repoRoot 'Raphael') -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
    Select-String -Pattern '(?<!//.*)(Console\.WriteLine|Debug\.Log)\s*\(' -ErrorAction SilentlyContinue
if ($strayLogs) {
    foreach ($hit in $strayLogs) { Warn "$($hit.Path):$($hit.LineNumber) — use Plugin.LogInstance / Core.Log instead." }
} else {
    Pass "No stray Console/Debug log calls."
}

# ---- Release-only checks below ----
if ($Mode -eq 'Release') {

    # 6. CHANGELOG.md has a "## <currentVersion>" entry at the top.
    $changelog = Join-Path $repoRoot 'CHANGELOG.md'
    if ((Test-Path $changelog) -and $csprojVersion) {
        $head = (Get-Content $changelog -TotalCount 20) -join "`n"
        if ($head -match "(?m)^##\s+$([regex]::Escape($csprojVersion))\b") {
            Pass "CHANGELOG.md has an entry for $csprojVersion."
        } else {
            Fail "CHANGELOG.md missing '## $csprojVersion' header near top."
        }
    }

    # 7. icon.png must exist at Raphael/icon.png and be 256x256.
    $icon = Join-Path $repoRoot 'Raphael\icon.png'
    if (-not (Test-Path $icon)) {
        Fail "Missing $icon (Thunderstore requires a 256x256 PNG)."
    } else {
        try {
            $bytes = [System.IO.File]::ReadAllBytes($icon)
            # Validate PNG signature.
            if ($bytes.Length -lt 24 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
                Fail "$icon is not a valid PNG."
            } else {
                # IHDR is the first chunk; width/height are big-endian uint32 at offsets 16 and 20.
                $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
                $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
                if ($w -eq 256 -and $h -eq 256) {
                    Pass "icon.png is 256x256."
                } else {
                    Fail "icon.png is ${w}x${h}, must be exactly 256x256."
                }
            }
        } catch {
            Fail "Could not read $icon : $($_.Exception.Message)"
        }
    }

    # 8. Git working tree clean on version-bearing files.
    $dirty = & git status --porcelain -- `
        'Raphael/Raphael.csproj' `
        'Raphael/thunderstore.toml' `
        'CHANGELOG.md' 2>$null
    if ($LASTEXITCODE -eq 0) {
        if ([string]::IsNullOrWhiteSpace($dirty)) {
            Pass "Version-bearing files are clean in git."
        } else {
            Fail "Uncommitted changes on version-bearing files:`n$dirty"
        }
    }

    # 9. Release build (warning count surfaced, not enforced).
    Write-Host "  ..    Running 'dotnet build -c Release' (this can take a minute)..." -ForegroundColor DarkGray
    $buildOut = & dotnet build (Join-Path $repoRoot 'Raphael.sln') -c Release --nologo 2>&1
    if ($LASTEXITCODE -eq 0) {
        $warnCount = ($buildOut | Select-String -Pattern 'warning ' -SimpleMatch).Count
        Pass "Release build succeeded ($warnCount warnings)."
    } else {
        Fail "Release build failed. Last 20 lines:`n$($buildOut[-20..-1] -join "`n")"
    }

    # 10. Built DLL AssemblyVersion must match csproj <Version>. Catches the
    #     v0.10.6 failure mode: csproj bumped to a new version but the DLL
    #     was built before the bump, so PLUGIN_VERSION (and the About tab)
    #     still showed the old version after deploy. Reads metadata only —
    #     does not load the assembly into the PS AppDomain.
    if ($csprojVersion) {
        $dll = Join-Path $repoRoot 'Raphael\bin\Release\net6.0\Raphael.dll'
        if (-not (Test-Path $dll)) {
            Fail "Built DLL not found at $dll — release build did not produce expected output."
        } else {
            try {
                $built = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
                $expected1 = $csprojVersion           # "0.10.7"
                $expected2 = "$csprojVersion.0"       # ".NET appends revision 0"
                if ($built -eq $expected1 -or $built -eq $expected2) {
                    Pass "DLL AssemblyVersion ($built) matches csproj <Version> ($csprojVersion)."
                } else {
                    Fail "DLL AssemblyVersion drift: built=$built, csproj=$csprojVersion. Did you forget to rebuild after bump-version.ps1?"
                }
            } catch {
                Warn "Could not inspect DLL AssemblyVersion: $($_.Exception.Message)"
            }
        }
    }

    # 11. Logic tests (0.72.0): the pure rule mirror, batch / quarantine, rail accordion, info parsing and form state.
    #     Not part of Raphael.sln, so the mod build and the package never include them.
    $testsDir = Join-Path $repoRoot 'tests\Raphael.Logic.Tests'
    if (Test-Path $testsDir) {
        Write-Host "  ..    Running logic tests (tests\Raphael.Logic.Tests)..." -ForegroundColor DarkGray
        $testOut = & dotnet run --project $testsDir -c Release --nologo 2>&1
        $summary = ($testOut | Select-String -Pattern 'Raphael.Logic.Tests:' -SimpleMatch | Select-Object -Last 1)
        if ($LASTEXITCODE -eq 0) {
            Pass "Logic tests passed ($($summary.Line.Trim()))."
        } else {
            Fail "Logic tests failed:`n$(($testOut | Select-String -Pattern 'FAIL' -SimpleMatch | ForEach-Object { $_.Line }) -join "`n")`n$($summary.Line)"
        }
    } else {
        Warn "tests\Raphael.Logic.Tests not found — logic tests skipped."
    }
}

# ---- Summary ----
Write-Host ('-' * 60)
if ($fails.Count -gt 0) {
    Write-Host "FAILED ($($fails.Count) failure$(if ($fails.Count -ne 1) { 's' }), $($warns.Count) warning$(if ($warns.Count -ne 1) { 's' }))" -ForegroundColor Red
    exit 1
} elseif ($warns.Count -gt 0) {
    Write-Host "OK with warnings ($($warns.Count))" -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "PASS" -ForegroundColor Green
    exit 0
}
