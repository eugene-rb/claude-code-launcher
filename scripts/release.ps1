<#
.SYNOPSIS
    Publishes ClaudeLauncher.App, packs it into a Velopack installer, and optionally uploads the
    release to GitHub Releases (eugene-rb/claude-code-launcher).

.PARAMETER Version
    Release version, e.g. "0.2.0". Must match <Version> in ClaudeLauncher.App.csproj.

.PARAMETER Upload
    Also upload the packed release to GitHub via `vpk upload github`. Requires $env:GITHUB_TOKEN
    (a PAT with `repo` scope) or an already-authenticated `gh` session won't be used here - vpk
    needs its own token.

.PARAMETER SkipPublish
    Skip `dotnet publish` and pack whatever is already in publish\win-x64 (useful when iterating on
    packaging only).

.EXAMPLE
    ./scripts/release.ps1 -Version 0.2.0
    ./scripts/release.ps1 -Version 0.2.0 -Upload
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Upload,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\ClaudeLauncher.App\ClaudeLauncher.App.csproj'
$iconPath = Join-Path $repoRoot 'src\ClaudeLauncher.App\Assets\app.ico'
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$releasesDir = Join-Path $repoRoot 'Releases'
$repoUrl = 'https://github.com/eugene-rb/claude-code-launcher'

if (-not $SkipPublish) {
    Write-Host "==> dotnet publish (win-x64, self-contained, v$Version)"
    dotnet publish $appProject -c Release -r win-x64 --self-contained `
        -o $publishDir `
        "/p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

# --packTitle is kept ASCII-only: passing Japanese text through PowerShell 5.1 to a native exe's argv
# mangled it into mojibake in the produced shortcut/uninstall-entry names (confirmed by inspecting the
# actual .lnk filenames on disk, not just console output) - the in-app UI is unaffected, it's Japanese
# throughout regardless of this.
Write-Host "==> vpk pack"
vpk pack `
    --packId ClaudeCodeLauncher `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ClaudeLauncher.App.exe `
    --packTitle "Claude Code Launcher" `
    --icon $iconPath `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "==> packed release contents:"
Get-ChildItem $releasesDir | Format-Table Name, Length

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) {
        throw "GITHUB_TOKEN environment variable is required for -Upload (a GitHub PAT with 'repo' scope)."
    }

    Write-Host "==> vpk upload github"
    # Uploads every file vpk pack produced (Setup.exe, the full/delta nupkgs, and the release feed
    # file) - the feed file is what makes in-app auto-update work, not just the installer, so this
    # must NOT be replaced with a hand-picked `gh release upload Setup.exe`.
    vpk upload github `
        --outputDir $releasesDir `
        --repoUrl $repoUrl `
        --token $env:GITHUB_TOKEN `
        --channel win `
        --publish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed" }
}

Write-Host "==> done"
