@echo off
rem Installs AxClaude for the current user: double-click it, no administrator rights, no PowerShell policy to change.
rem Next to install.ps1 (the extracted zip, or the installed folder) it runs that script and passes its arguments on,
rem for example: install.cmd -Uninstall. On its own, downloaded from the releases page, it fetches the latest release
rem from GitHub and installs it. PowerShell runs with the execution policy bypassed for this one run only.
rem Everything is one block: cmd.exe reads a block whole before running it, so this file may be deleted while it runs
rem (Uninstall from Settings, Apps runs it from the folder that the uninstall removes).
setlocal
(
    if exist "%~dp0install.ps1" (
        powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
    ) else (
        echo Getting the latest AxClaude release from GitHub...
        powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; [Net.ServicePointManager]::SecurityProtocol = 'Tls12'; $release = Invoke-RestMethod 'https://api.github.com/repos/KyleKeane/AxClaude/releases/latest' -Headers @{ 'User-Agent' = 'AxClaude-install' }; $asset = $release.assets | Where-Object { $_.name -like 'AxClaude-*-win-x64.zip' } | Select-Object -First 1; if (-not $asset) { throw 'The latest release has no zip.' }; $dir = Join-Path $env:TEMP 'AxClaude-install'; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null; $zip = Join-Path $dir $asset.name; Write-Host ('Downloading ' + $asset.name + '...'); Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing; Expand-Archive $zip $dir -Force; & (Join-Path $dir 'install.ps1'); $code = $LASTEXITCODE; Set-Location $env:TEMP; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue; exit $code"
    )
    if errorlevel 1 (
        echo.
        echo That did not work. The messages above say why.
    )
    echo.
    pause
    rem Leave without reading this file again: (goto) ends the batch at once and quietly, even once it is deleted.
    (goto) 2>nul
)
