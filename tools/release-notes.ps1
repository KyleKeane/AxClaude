<#
.SYNOPSIS
  Prints the CHANGELOG.md section of one version: the text under "## <version>" up to the next "## " heading.
  The release workflow uses it as the GitHub release notes, which the app shows in its update notice.

.EXAMPLE
  .\tools\release-notes.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [string]$Path
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is empty while parameter defaults are evaluated in Windows PowerShell 5.1, so the default is set here.
if (-not $Path) { $Path = Join-Path $PSScriptRoot '..\CHANGELOG.md' }

$lines = @(Get-Content $Path)
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match ('^## ' + [regex]::Escape($Version) + '(\s|$)')) {
        $start = $i + 1
        break
    }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG.md has no '## $Version' section. Add one before releasing."
    exit 1
}

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') {
        $end = $i
        break
    }
}

$section = if ($end -gt $start) { ($lines[$start..($end - 1)] -join "`n").Trim() } else { '' }
if (-not $section) {
    Write-Error "The '## $Version' section of CHANGELOG.md is empty."
    exit 1
}
Write-Output $section
