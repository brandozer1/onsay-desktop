# start.ps1 - builds and starts Onsay. Run it with: .\start.ps1
# The port runs on Node, from port/, installed first with: cd port; npm install
# A running Onsay is closed first, since it holds files the build replaces.

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { Write-Host "The .NET SDK is not installed. See README.md."; exit 1 }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Host "Node is not installed. Get it from nodejs.org"; exit 1 }
if (-not (Test-Path "$PSScriptRoot\port\node_modules")) { Write-Host "Install the port first: cd port; npm install"; exit 1 }

Get-Process Onsay -ErrorAction SilentlyContinue | Stop-Process -Force
Set-Location $PSScriptRoot
& $dotnet run
