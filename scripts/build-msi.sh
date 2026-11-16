#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet test "$root/EmergencyFolder.sln" -c Release
dotnet publish "$root/src/EmergencyFolder.App/EmergencyFolder.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$root/artifacts/publish"

mkdir -p "$root/dist"
wixl -a x64 \
  -D "ExePath=$root/artifacts/publish/Emergency folder.exe" \
  -D "IconPath=$root/icon.ico" \
  -o "$root/dist/Emergency-folder-2.1.0.msi" \
  "$root/installer/Package.wxs"
python3 "$root/scripts/finish-msi.py" "$root/dist/Emergency-folder-2.1.0.msi"
