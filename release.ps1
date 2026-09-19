<#
.SYNOPSIS
  Releases a new version of AxClaude: sets the version, commits, tags and pushes. GitHub Actions then builds, tests,
  publishes the zip and creates the GitHub release that installed copies of AxClaude update themselves from.

.DESCRIPTION
  Before running it, add a "## <version> - <date>" section to CHANGELOG.md: its text becomes the release notes and
  is what the update notice in the app shows. Everything else must be committed. The script then
    1. runs the unit tests,
    2. sets <Version> in src/AxClaude/AxClaude.csproj,
    3. commits CHANGELOG.md and the project file as "Release <version>",
    4. tags v<version> and pushes main and the tag.
  The push starts .github/workflows/release.yml. Follow it with `gh run watch`, or on the Actions page. When it is
  done, the release is at https://github.com/KyleKeane/AxClaude/releases and the app offers it as an update.

.EXAMPLE
  .\release.ps1 1.0.1
  .\release.ps1 1.0.1 -NoPush     # commit and tag only; push later with: git push origin main v1.0.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\AxClaude\AxClaude.csproj'
$changelog = Join-Path $root 'CHANGELOG.md'
$tag = "v$Version"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "The version must be three numbers, like 1.0.1 (got '$Version')."
    exit 1
}

$branch = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') {
    Write-Host "Release from main (you are on '$branch')."
    exit 1
}

$dirty = @(git -C $root status --porcelain) | Where-Object { $_ -and ($_.Substring(3) -ne 'CHANGELOG.md') }
if ($dirty.Count -gt 0) {
    Write-Host "Commit or stash these first; only CHANGELOG.md may be uncommitted:"
    $dirty | ForEach-Object { Write-Host "  $_" }
    exit 1
}

if (git -C $root tag --list $tag) {
    Write-Host "The tag $tag already exists. Pick the next version."
    exit 1
}

if (-not (Select-String -Path $changelog -Pattern ('^## ' + [regex]::Escape($Version) + '(\s|$)') -Quiet)) {
    Write-Host "CHANGELOG.md has no '## $Version' section. Add one (its text becomes the release notes), then run this again."
    exit 1
}

Write-Host "Running the tests..."
dotnet test (Join-Path $root 'AxClaude.sln') --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "The tests failed. Nothing was changed."
    exit $LASTEXITCODE
}

$xml = Get-Content $project -Raw
$updated = $xml -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
if ($updated -eq $xml -and $xml -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
    Write-Host "No <Version> element was found in $project."
    exit 1
}
Set-Content -Path $project -Value $updated -Encoding utf8 -NoNewline

git -C $root add -- CHANGELOG.md src/AxClaude/AxClaude.csproj
git -C $root commit -q -m "Release $Version"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git -C $root tag -a $tag -m "AxClaude $Version"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Committed and tagged $tag."

if ($NoPush) {
    Write-Host "Not pushed. When ready: git push origin main $tag"
    exit 0
}

git -C $root push origin main $tag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Pushed. GitHub Actions is building the release: https://github.com/KyleKeane/AxClaude/actions"
Write-Host "Follow it with: gh run watch    (the release then appears at https://github.com/KyleKeane/AxClaude/releases)"
