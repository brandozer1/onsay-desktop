#!/bin/sh
# release.sh - makes a release of Onsay for Windows on a Linux server and puts it in Onsay's storage, a RustFS bucket.
# Run it where Docker is, with the version, once that version's tag, like v0.2.0, is pushed:
#
#   ./release.sh 0.2.0           built in a throwaway container, then uploaded
#   ./release.sh upload 0.2.0    Releases/windows/0.2.0/ beside it uploaded as it is, for a release release.ps1 made
#                                elsewhere, like on Windows, run from Git Bash there
#
# The container holds everything a release needs, zig, the .NET SDK, Wine and Inno Setup, runs release.ps1 on
# onsay-desktop at that tag and lingo-client's main, uploads what it made, and is deleted: nothing keeps running and nothing
# is installed on the server itself. The keys go in .env beside it, never in git:
#
#   RUSTFS_KEY=...       an access key that may write to the bucket
#   RUSTFS_SECRET=...
#   RUSTFS_REGION=...    only when it isn't us-east-1
#
# In the bucket, each version's files go in windows/<version>/, as release.ps1 lays them out in Releases/, then the
# installer is copied to windows/Onsay-Setup.exe,
# a download link that never changes, and windows/release.json is written last, naming the newest, so a release half
# uploaded is never offered. Onsay's server reads it from there (onsay server routes/updates.js).
set -eu

STORAGE=https://storage.brandongamez.com/onsay   # the bucket, readable by anyone

########## UPLOADING ##########

# One file into the bucket, signed with the keys as S3 asks (curl's own --aws-sigv4).
put() {
  case "$1" in *.json) type=application/json ;; *) type=application/octet-stream ;; esac
  curl -fsS --aws-sigv4 "aws:amz:${RUSTFS_REGION:-us-east-1}:s3" --user "$RUSTFS_KEY:$RUSTFS_SECRET" \
    -H "x-amz-content-sha256: UNSIGNED-PAYLOAD" -H "Content-Type: $type" -T "$1" "$STORAGE/$2"
  echo "  $2"
}

# Every file of the release in folder, the version's own first and release.json last.
upload() {
  cd "$2"
  grep -q "\"$1\"" release.json || { echo "$2/release.json isn't version $1"; exit 1; }
  for f in *; do put "$f" "windows/$1/$f"; done
  put Onsay-Setup.exe windows/Onsay-Setup.exe
  put release.json windows/release.json
  echo "Onsay $1 is out: $STORAGE/windows/Onsay-Setup.exe"
}

########## ON THE SERVER, OR UPLOADING ONE MADE ELSEWHERE ##########

if [ "${1:-}" != "inside" ]; then
  cd "$(dirname "$0")"
  # .env read as NAME=value lines, never run, so a key may hold any character.
  if [ -f .env ]; then
    while IFS= read -r line || [ -n "$line" ]; do
      line=$(printf '%s' "$line" | tr -d '\r')
      case "$line" in RUSTFS_*=*) export "${line%%=*}=${line#*=}" ;; esac
    done < .env
  fi
  : "${RUSTFS_KEY:?Put RUSTFS_KEY in .env beside release.sh}" "${RUSTFS_SECRET:?Put RUSTFS_SECRET in .env beside release.sh}"
  if [ "${1:-}" = "upload" ]; then
    VERSION=${2:?Say the version, like ./release.sh upload 0.2.0}
    upload "$VERSION" "$PWD/Releases/windows/$VERSION"
    exit 0
  fi
  VERSION=${1:?Say the version, like ./release.sh 0.2.0}
  docker build -t onsay-release - <<'IMAGE'
FROM mcr.microsoft.com/dotnet/sdk:8.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 WINEDEBUG=-all WINEPREFIX=/opt/wine WINEDLLOVERRIDES="mscoree,mshtml=" ISCC=/opt/wine/drive_c/inno/ISCC.exe
RUN dpkg --add-architecture i386 && apt-get update && apt-get install -y --no-install-recommends \
      wine wine64 wine32:i386 xvfb xauth imagemagick git curl xz-utils ca-certificates && rm -rf /var/lib/apt/lists/*
RUN curl -fsSL -o /tmp/zig.tar.xz https://ziglang.org/download/0.13.0/zig-linux-x86_64-0.13.0.tar.xz \
 && echo "d45312e61ebcc48032b77bc4cf7fd6915c11fa16e4aad116b66c9468211230ea  /tmp/zig.tar.xz" | sha256sum -c - \
 && tar -xJf /tmp/zig.tar.xz -C /opt && ln -s /opt/zig-linux-x86_64-0.13.0/zig /usr/local/bin/zig && rm /tmp/zig.tar.xz
RUN curl -fsSL -o /tmp/is.exe https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe \
 && echo "a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c  /tmp/is.exe" | sha256sum -c - \
 && xvfb-run -a wine /tmp/is.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- '/DIR=C:\inno' \
 && test -f "$ISCC" && rm /tmp/is.exe
IMAGE
  exec docker run --rm -e RUSTFS_KEY -e RUSTFS_SECRET -e RUSTFS_REGION -v "$PWD/release.sh:/release.sh:ro" \
    onsay-release sh /release.sh inside "$VERSION"
fi

########## INSIDE THE CONTAINER ##########

VERSION=$2
git clone -q --depth 1 --branch "v$VERSION" https://github.com/brandozer1/onsay-desktop.git /work/onsay-desktop
git clone -q --depth 1 https://github.com/brandozer1/lingo-client.git /work/lingo-client
pwsh /work/onsay-desktop/release.ps1 "$VERSION"
upload "$VERSION" "/work/onsay-desktop/Releases/windows/$VERSION"
