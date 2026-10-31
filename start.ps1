# start.ps1 - builds and starts Onsay. Run it with: .\start.ps1
# The lingo client comes from the lingo-client project beside this one: build it first with ..\lingo-client\core\build.ps1.
# A running Onsay and its own lingo client (never a test one) are closed first, since it holds files the build replaces.

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { Write-Host "The .NET 8 SDK is not installed. See README.md."; exit 1 }
if (-not (Test-Path "$PSScriptRoot\..\lingo-client\core\bin\onsay-core.exe")) { Write-Host "Build the lingo client first: ..\lingo-client\core\build.ps1"; exit 1 }

Get-Process Onsay -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process onsay-core -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$PSScriptRoot\bin\*" } | Stop-Process -Force
Set-Location $PSScriptRoot
& $dotnet run
