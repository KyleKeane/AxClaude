<#
.SYNOPSIS
  Builds the distributable AxClaude: a self-contained single-file executable with the installer and the user guide,
  zipped for sending, and installs it for the current user unless -NoInstall is given.

.DESCRIPTION
  Output: publish\win-x64\ (AxClaude.exe, install.ps1, README.md, LICENSE) and publish\AxClaude-<version>-win-x64.zip.
  Send the zip to someone: they extract it and run install.ps1 (see README.md inside). Without -NoInstall the
  script then runs install.ps1 from publish\win-x64 on this machine (Start menu entry, File Explorer entry and
  the axclaude console command; see install.ps1 -? for details and -Uninstall).

.EXAMPLE
  .\publish.ps1              # build the zip and install here
  .\publish.ps1 -NoInstall   # build the zip only

  If PowerShell refuses to run scripts: powershell -ExecutionPolicy Bypass -File .\publish.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish\win-x64'

Write-Host "Publishing AxClaude (Release, win-x64, self-contained, single file)..."
dotnet publish "$root\src\AxClaude\AxClaude.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed."
    exit $LASTEXITCODE
}

$exe = Join-Path $out 'AxClaude.exe'
Copy-Item (Join-Path $root 'install.ps1') $out -Force
Copy-Item (Join-Path $root 'LICENSE') $out -Force
Copy-Item (Join-Path $root 'docs\user-guide.md') (Join-Path $out 'README.md') -Force
Get-ChildItem $out -Filter '*.pdb' | Remove-Item -Force

$version = (Get-Item $exe).VersionInfo.FileVersion
if ($version -match '^(\d+\.\d+\.\d+)') { $version = $Matches[1] }
$zip = Join-Path $root "publish\AxClaude-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host ("Published {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host ("Zip for sending: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

if ($NoInstall) {
    exit 0
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $out 'install.ps1')
exit $LASTEXITCODE
