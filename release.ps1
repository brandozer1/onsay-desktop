# release.ps1 - makes a release of Onsay for Windows 7 and later, 32 and 64-bit, in Releases\: Onsay-Setup.exe, the
# installer people download (setup.iss), and what installed copies update from (src/System/Updates.cs): release.json,
# naming the version and each file of it with its sha256, and each file under the name release.json gives it. Onsay
# signs release.json as it hands it out (onsay server routes/updates.js). Run it with: .\release.ps1 0.2.0
# A release people get is made on GitHub by .github/workflows/release.yml, which runs this and puts Releases\ on the
# release page: push a tag like v0.2.0, or run the action from the Actions tab. Installed copies update from there.
#
# Needs the .NET SDK, zig for the lingo client and the launcher (see ..\lingo-client\core\build.ps1), and Inno Setup 6,
# from jrsoftware.org, which the release action installs first.
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { Write-Host "Inno Setup 6 isn't installed. Get it from jrsoftware.org"; exit 1 }
$zig = (Get-Command zig -ErrorAction SilentlyContinue).Source
if (-not $zig) { $zig = Get-ChildItem "$env:LOCALAPPDATA\zig" -Recurse -Filter zig.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
if (-not $zig) { Write-Host "zig wasn't found. Get it from ziglang.org/download"; exit 1 }

# The lingo client and its floors, then the app, then the launcher, 32-bit so it runs on every Windows.
$client = "$PSScriptRoot\..\lingo-client\core"
& "$client\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
if (Test-Path Releases) { Remove-Item Releases -Recurse -Force }
& $dotnet publish -c Release -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force obj\launcher, Releases | Out-Null
& $zig cc -target x86-windows-gnu -Os -s "-Wl,--subsystem,windows" launcher.c launcher.rc -o obj\launcher\Onsay.exe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# What installed copies update from: every file of the app, by its place in app-<version>, and the floor for each kind of
# computer, each in Releases\ under a name with no folders, which is how GitHub keeps a release's files, starting
# update- so the installer is the one file a person picks there.
$files = @()
function ship($from, $path, $name, $arch) {
    Copy-Item $from "Releases\$name"
    $file = [ordered]@{ path = $path; name = $name; sha256 = (Get-FileHash $from -Algorithm SHA256).Hash.ToLower(); size = (Get-Item $from).Length }
    if ($arch) { $file.arch = $arch }
    $script:files += $file
}
$published = (Resolve-Path publish).Path
foreach ($f in Get-ChildItem publish -Recurse -File | Where-Object { $_.Name -ne "floor.exe" -and $_.Extension -ne ".pdb" }) {
    $path = $f.FullName.Substring($published.Length + 1).Replace('\', '/')
    ship $f.FullName $path ("update-" + $path.Replace("/", ".")) $null
}
ship "$client\bin\windows\floor.exe" "floor.exe" "update-floor-x64.exe" "x64"
ship "$client\bin\x86\windows\floor.exe" "floor.exe" "update-floor-x86.exe" "x86"
$release = [ordered]@{ version = $Version; files = $files } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText("$PSScriptRoot\Releases\release.json", $release, (New-Object Text.UTF8Encoding $false))

& $iscc "/DVersion=$Version" "/DApp=$published" "/DLauncher=$PSScriptRoot\obj\launcher\Onsay.exe" "/DFloor64=$client\bin\windows\floor.exe" "/DFloor32=$client\bin\x86\windows\floor.exe" setup.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Made Onsay $Version in Releases\."
