# release.ps1 - makes a release of Onsay for Windows, in Releases\: Onsay-win-Setup.exe, the installer people download,
# and the packages the installed app updates itself from (src/System/Updates.cs). Run it with: .\release.ps1 0.2.0
# Then upload everything in Releases\ to the feed, Feed in src/System/Updates.cs, keeping what is there: the app finds
# its updates by the releases files listed in it. .github/workflows/release.yml does the same on a tag like v0.2.0.
#
# Needs the .NET SDK, zig for the core (see ..\onsay\core\build.ps1), and Velopack's vpk, which needs .NET 8 or newer:
#   dotnet tool install -g vpk
# The installer puts him in %LocalAppData%\Onsay for this person alone, with no admin rights, adds him to the Start
# menu and the desktop, and opens him. Uninstalling him is in Windows' Installed apps.
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
$vpk = (Get-Command vpk -ErrorAction SilentlyContinue).Source
if (-not $vpk) { Write-Host "Velopack's vpk isn't installed: dotnet tool install -g vpk"; exit 1 }

& "$PSScriptRoot\..\onsay\core\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
& $dotnet publish -c Release -r win-x64 --self-contained -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $vpk pack --packId Onsay --packVersion $Version --packDir publish --mainExe Onsay.exe --packTitle Onsay --packAuthors Onsay --outputDir Releases
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Made Onsay $Version in Releases\. Upload everything there to the feed."
