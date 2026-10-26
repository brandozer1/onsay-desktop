# release.ps1 - makes a release of Onsay for Windows, in Releases\: Onsay-win-Setup.exe, the installer people download,
# and the packages the installed app updates itself from (src/System/Updates.cs). Run it with: .\release.ps1 0.2.0
# A release people get is made on GitHub by .github/workflows/release.yml, which runs this and puts Releases\ on the
# release page: push a tag like v0.2.0, or run the action from the Actions tab. Installed copies update from there.
#
# Needs the .NET SDK, zig for the lingo client (see ..\lingo-client\core\build.ps1), and Velopack's vpk, which needs .NET 8 or newer:
#   dotnet tool install -g vpk
# The installer shows splash.png while it puts him in %LocalAppData%\Onsay for this person alone, with no admin rights,
# adds him to the Start menu and the desktop, and opens him, where setting him up (src/Windows/SetupWindow.cs) asks the
# rest: his look, his shortcuts and starting with Windows. Uninstalling him is in Windows' Installed apps.
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not $env:DOTNET_ROOT) { $env:DOTNET_ROOT = Split-Path $dotnet }   # so vpk finds a .NET installed for this person alone
$vpk = (Get-Command vpk -ErrorAction SilentlyContinue).Source
if (-not $vpk) { Write-Host "Velopack's vpk isn't installed: dotnet tool install -g vpk"; exit 1 }

& "$PSScriptRoot\..\lingo-client\core\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
& $dotnet publish -c Release -r win-x64 --self-contained -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $vpk pack --runtime win-x64 --packId Onsay --packVersion $Version --packDir publish --mainExe Onsay.exe --packTitle Onsay --packAuthors Onsay --icon onsay.ico --splashImage splash.png --outputDir Releases
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Made Onsay $Version in Releases\."
