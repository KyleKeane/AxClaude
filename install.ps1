<#
.SYNOPSIS
  Installs AxClaude for the current user, or removes it with -Uninstall. No administrator rights are needed.

.DESCRIPTION
  Run it from the folder that holds AxClaude.exe: the extracted zip, or publish\win-x64 after publish.ps1.
  It copies AxClaude to %LOCALAPPDATA%\Programs\AxClaude and creates three ways to start it:
    - a Start menu entry called AxClaude,
    - an "Open in AxClaude" entry in the right-click menu of folders in File Explorer (and of a folder's background),
    - the axclaude command for consoles, a one-line shim in %USERPROFILE%\.local\bin, the folder that Claude Code's
      installer already puts on PATH.
  -Uninstall removes all of that. The settings file in %APPDATA%\AxClaude and the log in %LOCALAPPDATA%\AxClaude stay.

.EXAMPLE
  .\install.ps1                 # install or update
  .\install.ps1 -Uninstall      # remove
  .\install.ps1 -NoContextMenu  # install without the File Explorer entry

  If PowerShell refuses to run scripts: powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoContextMenu,
    [switch]$NoStartMenu
)

$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\AxClaude'
$exe = Join-Path $target 'AxClaude.exe'
$bin = Join-Path $env:USERPROFILE '.local\bin'
$shim = Join-Path $bin 'axclaude.cmd'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'AxClaude.lnk'
$shellKeys = @(
    'HKCU:\Software\Classes\Directory\shell\AxClaude',
    'HKCU:\Software\Classes\Directory\Background\shell\AxClaude'
)

if ($Uninstall) {
    foreach ($key in $shellKeys) {
        if (Test-Path $key) { Remove-Item $key -Recurse -Force }
    }
    if (Test-Path $startMenu) { Remove-Item $startMenu -Force }
    if (Test-Path $shim) { Remove-Item $shim -Force }
    if (Test-Path $target) {
        try {
            Remove-Item $target -Recurse -Force
        }
        catch {
            Write-Host "Could not remove $target (is AxClaude running?): $($_.Exception.Message)"
            exit 1
        }
    }
    Write-Host "AxClaude was removed. Settings in $env:APPDATA\AxClaude and the log in $env:LOCALAPPDATA\AxClaude were kept."
    exit 0
}

$sourceExe = Join-Path $source 'AxClaude.exe'
if (-not (Test-Path $sourceExe)) {
    Write-Host "AxClaude.exe was not found next to this script in $source."
    exit 1
}

New-Item -ItemType Directory -Force $target | Out-Null
$sameFolder = [string]::Equals((Resolve-Path $source).Path.TrimEnd('\'), $target.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
if (-not $sameFolder) {
    try {
        foreach ($name in 'AxClaude.exe', 'README.md', 'LICENSE', 'install.ps1') {
            $file = Join-Path $source $name
            if (Test-Path $file) { Copy-Item $file $target -Force }
        }
    }
    catch {
        Write-Host "Could not copy into $target (is AxClaude running from there? Close it and run this again): $($_.Exception.Message)"
        exit 1
    }
}

# Files extracted from a downloaded zip carry the "downloaded from the internet" mark, which makes SmartScreen warn
# about an unknown publisher on every start. The installed copies lose the mark, so the warning does not come back.
try { Get-ChildItem $target -File | Unblock-File -ErrorAction Stop } catch { }

New-Item -ItemType Directory -Force $bin | Out-Null
Set-Content -Path $shim -Value @('@echo off', "`"$exe`" %*") -Encoding ascii

if (-not $NoStartMenu) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($startMenu)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.Description = 'Accessible front end for Claude Code'
    $link.IconLocation = "$exe,0"
    $link.Save()
}

if (-not $NoContextMenu) {
    foreach ($key in $shellKeys) {
        New-Item -Path $key -Force | Out-Null
        Set-ItemProperty -Path $key -Name '(default)' -Value 'Open in AxClaude'
        Set-ItemProperty -Path $key -Name 'Icon' -Value "`"$exe`",0"
        $command = Join-Path $key 'command'
        New-Item -Path $command -Force | Out-Null
        $argument = if ($key -like '*Background*') { '%V' } else { '%1' }
        Set-ItemProperty -Path $command -Name '(default)' -Value "`"$exe`" `"$argument`""
    }
}

Write-Host "Installed AxClaude to $target"
if (-not $NoStartMenu) { Write-Host "Start menu: AxClaude" }
if (-not $NoContextMenu) { Write-Host "File Explorer: right-click a folder, Open in AxClaude" }
Write-Host "Console: axclaude, or axclaude C:\path\to\project (shim in $shim)"
if (-not (($env:Path -split ';') -contains $bin)) {
    Write-Host "Note: $bin is not on PATH in this console. Open a new console, or add it, before using the axclaude command."
}
