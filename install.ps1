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
  It also lists AxClaude in Settings, Apps (Add or remove programs), whose Uninstall runs install.cmd -Uninstall.
  -Uninstall removes all of that. The settings file in %APPDATA%\AxClaude and the log in %LOCALAPPDATA%\AxClaude stay.

.EXAMPLE
  .\install.ps1                 # install or update
  .\install.ps1 -Uninstall      # remove
  .\install.ps1 -NoContextMenu  # install without the File Explorer entry
  .\install.ps1 -NoStartMenu    # install without the Start menu entry

  install.cmd, next to this script, runs it with the execution policy bypassed: double-click it, or install.cmd -Uninstall.
  Downloaded on its own from the releases page, install.cmd fetches the latest release and runs this script from it.

  AxClaude's own Update now (Help menu) runs the downloaded version's copy of this script as
    install.ps1 -WaitForProcess <pid> -Start <folder> [-ContinueConversation] -LogFile <file>
  which waits for the running AxClaude to end, installs, and starts the new one on the folder.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoContextMenu,
    [switch]$NoStartMenu,
    [int]$WaitForProcess,
    [string]$Start,
    [switch]$ContinueConversation,
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'

function Say([string]$text) {
    Write-Host $text
    if ($LogFile) {
        try { Add-Content -Path $LogFile -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $text" -Encoding utf8 } catch { }
    }
}

# Any error that nothing below handles is said and logged: the updater runs this script without a window, so the
# log is the only place it would show.
trap {
    Say "AxClaude was not installed: $($_.Exception.Message)"
    exit 1
}

if ($WaitForProcess) {
    Say "Waiting for AxClaude (process $WaitForProcess) to close..."
    try { Wait-Process -Id $WaitForProcess -Timeout 120 -ErrorAction Stop } catch { }
    if (Get-Process -Id $WaitForProcess -ErrorAction SilentlyContinue) {
        Say "AxClaude is still running after two minutes. The update was not installed; start it from the Help menu again."
        exit 1
    }
}

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
# The entry in Settings, Apps (Add or remove programs) for this user; its Uninstall runs install.cmd -Uninstall.
$appsKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AxClaude'

function Test-Running {
    [bool](Get-Process -Name AxClaude -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}

# Removes everything an installation puts on the machine, whatever version made it: every installer since 1.0.0 used
# these places, and those before 1.5.0 wrote no Apps entry. The settings and the log stay. Windows can hold the
# executable for a moment after AxClaude exits, so the folder is tried again for a few seconds.
function Remove-Installation {
    for ($attempt = 1; Test-Path $target; $attempt++) {
        try { Remove-Item $target -Recurse -Force }
        catch {
            if ($attempt -ge 10) { throw }
            Start-Sleep -Seconds 1
        }
    }
    foreach ($item in $shellKeys + $appsKey + $startMenu + $shim) {
        if (Test-Path $item) { Remove-Item $item -Recurse -Force }
    }
}

if ($Uninstall) {
    # A running AxClaude holds its folder: then nothing is removed, so the Apps entry and install.cmd stay to try again.
    if (Test-Running) {
        Say "AxClaude is still running, so nothing was removed. Close it and uninstall again."
        exit 1
    }

    Remove-Installation
    Say "AxClaude was removed. Settings in $env:APPDATA\AxClaude and the log in $env:LOCALAPPDATA\AxClaude were kept."
    exit 0
}

$sourceExe = Join-Path $source 'AxClaude.exe'
if (-not (Test-Path $sourceExe)) {
    Say "AxClaude.exe was not found next to this script in $source."
    exit 1
}

$sameFolder = [string]::Equals((Resolve-Path $source).Path.TrimEnd('\'), $target.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
if (-not $sameFolder) {
    # A previous installation, of any version, is removed completely first, so nothing of it stays behind; then this
    # one is installed fresh. The updater comes here too, after the running AxClaude has closed.
    $found = @($exe, $startMenu, $shim) + $shellKeys + $appsKey | Where-Object { Test-Path $_ }
    if ($found) {
        if (Test-Running) {
            Say "AxClaude is still running, so nothing was changed. Close it and run this again."
            exit 1
        }

        $what = if (Test-Path $exe) { 'AxClaude ' + ((Get-Item $exe).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*', '$1') } else { 'what is left of an earlier AxClaude' }
        $how = if (Test-Path $appsKey) { '' } else { ' (installed by the earlier installer)' }
        Say "Removing $what$how before installing..."
        Remove-Installation
    }

    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($name in 'AxClaude.exe', 'README.md', 'LICENSE', 'install.ps1', 'install.cmd') {
        $file = Join-Path $source $name
        if (Test-Path $file) { Copy-Item $file $target -Force }
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

New-Item -Path $appsKey -Force | Out-Null
$uninstallCmd = Join-Path $target 'install.cmd'
$appsUninstall = if (Test-Path $uninstallCmd) { "`"$uninstallCmd`" -Uninstall" }
    else { "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $target 'install.ps1')`" -Uninstall" }
$version = (Get-Item $exe).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*', '$1'
$entries = [ordered]@{
    DisplayName = 'AxClaude'
    DisplayVersion = $version
    Publisher = 'Dr. Kyle Keane'
    DisplayIcon = "`"$exe`",0"
    InstallLocation = $target
    UninstallString = $appsUninstall
    URLInfoAbout = 'https://github.com/KyleKeane/AxClaude'
}
foreach ($name in $entries.Keys) { Set-ItemProperty -Path $appsKey -Name $name -Value $entries[$name] }
foreach ($name in 'NoModify', 'NoRepair') { New-ItemProperty -Path $appsKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
New-ItemProperty -Path $appsKey -Name 'EstimatedSize' -Value ([int]((Get-Item $exe).Length / 1KB)) -PropertyType DWord -Force | Out-Null

Say "Installed AxClaude to $target"
Say "Settings, Apps: AxClaude, with Uninstall"
if (-not $NoStartMenu) { Say "Start menu: AxClaude" }
if (-not $NoContextMenu) { Say "File Explorer: right-click a folder, Open in AxClaude" }
Say "Console: axclaude, or axclaude C:\path\to\project (shim in $shim)"
if (-not (($env:Path -split ';') -contains $bin)) {
    Say "Note: $bin is not on PATH in this console. Open a new console, or add it, before using the axclaude command."
}

if ($Start) {
    $arguments = @("`"$Start`"")
    if ($ContinueConversation) { $arguments += @('--', '--continue') }
    Say "Starting AxClaude on $Start"
    Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $target
}
