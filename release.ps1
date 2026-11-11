# release.ps1 - makes a release of Onsay for Windows 7 and later, 32 and 64-bit, in Releases/windows/<version>/, laid
# out as it goes in Onsay's storage: Onsay-Setup.exe, the installer people download (setup.iss), and what installed
# copies update from (src/System/Updates.cs): release.json, naming the version and each file of it with its sha256, and
# each file under the name release.json gives it. Onsay signs release.json as it hands it out (onsay server
# routes/updates.js). Run it with: ./release.ps1 0.2.0
# It runs on Windows or Linux, and release.sh puts what it made in Onsay's storage.
#
# Needs the .NET SDK, zig for the lingo client and the launcher (see ../lingo-client/core/build.ps1), and Inno Setup 6,
# from jrsoftware.org: on Linux under Wine, at $env:ISCC, with ImageMagick for the splash.
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$here = $PSScriptRoot -replace '\\', '/'
$windows = $env:OS -eq "Windows_NT"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"   # .NET collects nothing from a build

function stop($why) { Write-Host "release.ps1 stopped: $why"; exit 1 }
trap { stop "line $($_.InvocationInfo.ScriptLineNumber): $_" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
$iscc = $env:ISCC
if (-not $iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }
if (-not $iscc -and $windows) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not $iscc -or -not (Test-Path $iscc)) { stop "Inno Setup 6 isn't installed. Get it from jrsoftware.org" }
$zig = (Get-Command zig -ErrorAction SilentlyContinue).Source
if (-not $zig -and $windows) { $zig = Get-ChildItem "$env:LOCALAPPDATA\zig" -Recurse -Filter zig.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
if (-not $zig) { stop "zig wasn't found. Get it from ziglang.org/download" }

# The lingo client and its floors, then the app, then the launcher, 32-bit so it runs on every Windows.
$client = "$here/../lingo-client/core"
& "$client/build.ps1"
if ($LASTEXITCODE -ne 0) { stop "The lingo client didn't build" }
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
if (Test-Path Releases) { Remove-Item Releases -Recurse -Force }
& $dotnet publish -c Release -p:Version=$Version -p:EnableWindowsTargeting=true -o publish
if ($LASTEXITCODE -ne 0) { stop "The app didn't build" }
$out = "Releases/windows/$Version"   # this release, as it goes in the storage
New-Item -ItemType Directory -Force obj/launcher, $out | Out-Null
& $zig cc -target x86-windows-gnu -Os -s "-Wl,--subsystem,windows" launcher.c launcher.rc -o obj/launcher/Onsay.exe
if ($LASTEXITCODE -ne 0) { stop "The launcher didn't build" }

# The splash as a 24-bit BMP, the picture every Inno Setup shows: by Windows itself, else by ImageMagick.
if ($windows) {
    Add-Type -AssemblyName System.Drawing
    $png = [System.Drawing.Image]::FromFile("$here/splash.png")
    $bmp = New-Object System.Drawing.Bitmap $png.Width, $png.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $paint = [System.Drawing.Graphics]::FromImage($bmp)
    $paint.DrawImage($png, 0, 0, $png.Width, $png.Height)
    $bmp.Save("$here/obj/splash.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $paint.Dispose(); $bmp.Dispose(); $png.Dispose()
} else {
    convert splash.png -type TrueColor BMP3:obj/splash.bmp
    if ($LASTEXITCODE -ne 0) { stop "The splash couldn't be made a BMP" }
}

# What installed copies update from: every file of the app, by its place in app-<version>, and the floor for each kind of
# computer, each in $out under a name with no folders, starting update- so the installer is the one file a person
# picks there.
$files = @()
function ship($from, $path, $name, $arch) {
    Copy-Item $from "$out/$name"
    $file = [ordered]@{ path = $path; name = $name; sha256 = (Get-FileHash $from -Algorithm SHA256).Hash.ToLower(); size = (Get-Item $from).Length }
    if ($arch) { $file.arch = $arch }
    $script:files += $file
}
$published = (Resolve-Path publish).Path
foreach ($f in Get-ChildItem publish -Recurse -File | Where-Object { $_.Name -ne "floor.exe" -and $_.Extension -ne ".pdb" }) {
    $path = $f.FullName.Substring($published.Length + 1).Replace('\', '/')
    ship $f.FullName $path ("update-" + $path.Replace("/", ".")) $null
}
ship "$client/bin/windows/floor.exe" "floor.exe" "update-floor-x64.exe" "x64"
ship "$client/bin/x86/windows/floor.exe" "floor.exe" "update-floor-x86.exe" "x86"

# The installer. Under Wine, each path as Windows sees it, the Linux root being drive Z:.
function seen($path) { if ($windows) { $path } else { "Z:" + (($path -replace '\\', '/') -replace '/', '\') } }
$defines = "/DVersion=$Version", "/DApp=$(seen $published)", "/DLauncher=$(seen "$here/obj/launcher/Onsay.exe")",
    "/DFloor64=$(seen "$client/bin/windows/floor.exe")", "/DFloor32=$(seen "$client/bin/x86/windows/floor.exe")",
    "/DSplash=$(seen "$here/obj/splash.bmp")", "/O$(seen "$here/$out")"
if ($windows) { & $iscc @defines setup.iss } else { & xvfb-run -a wine $iscc @defines setup.iss }
if ($LASTEXITCODE -ne 0) { stop "The installer wasn't made" }

# release.json, written last: the version and its files, which Onsay signs, and for the website, the installer's name and
# size and the day it was made.
$setup = [ordered]@{ name = "Onsay-Setup.exe"; size = (Get-Item "$out/Onsay-Setup.exe").Length }
$release = [ordered]@{ version = $Version; made = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd"); installer = $setup; files = $files } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText("$here/$out/release.json", $release, (New-Object Text.UTF8Encoding $false))
Write-Host "Made Onsay $Version in $out."
