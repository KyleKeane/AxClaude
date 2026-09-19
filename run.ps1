<#
.SYNOPSIS
  Builds AxClaude and starts it on a project folder.

.EXAMPLE
  .\run.ps1                       # current folder is the project
  .\run.ps1 C:\src\myproject      # that folder is the project
  .\run.ps1 C:\src\myproject -- --resume     # extra arguments go to claude (default: --continue)
  .\run.ps1 -NoBuild              # skip the build, just start
  .\run.ps1 -Test                 # run the unit tests instead

  If PowerShell refuses to run scripts: powershell -ExecutionPolicy Bypass -File .\run.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Project = (Get-Location).Path,

    [switch]$NoBuild,
    [switch]$Test,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ClaudeArgs
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if ($Test) {
    dotnet test "$root\AxClaude.sln" -nologo
    exit $LASTEXITCODE
}

if (-not $NoBuild) {
    Write-Host "Building AxClaude..."
    dotnet build "$root\src\AxClaude\AxClaude.csproj" -c Debug -nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed."
        exit $LASTEXITCODE
    }
}

$exe = Join-Path $root 'src\AxClaude\bin\Debug\net10.0-windows\AxClaude.exe'
if (-not (Test-Path $exe)) {
    Write-Host "AxClaude.exe was not found at $exe. Run without -NoBuild first."
    exit 1
}

$Project = (Resolve-Path $Project).Path
Write-Host "Starting AxClaude on $Project"
if ($ClaudeArgs -and $ClaudeArgs.Count -gt 0) {
    & $exe $Project '--' @ClaudeArgs
} else {
    & $exe $Project
}
