#!/usr/bin/env bash
# Builds all release files into ./release:
#   CocUpgradeTimer-<ver>-windows-x64.exe      single-file, no .NET install needed
#   CocUpgradeTimer-<ver>-linux-x64.tar.gz     binary + .desktop launcher + install.sh
#   CocUpgradeTimer-<ver>-android.apk
# Usage: scripts/build-release.sh [version]     (default: 1.3.0)
# Android needs a dotnet with the android workload; set ANDROID_DOTNET (default ~/.dotnet/dotnet).
set -euo pipefail
cd "$(dirname "$0")/.."
ver="${1:-1.3.0}"
dotnet="${DOTNET:-dotnet}"
android_dotnet="${ANDROID_DOTNET:-$HOME/.dotnet/dotnet}"
out="release"
rm -rf "$out" && mkdir -p "$out"

single=(-c Release --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
        -p:DebugType=none -p:Version="$ver")

echo "== Windows"
"$dotnet" publish CocTimeManager.Desktop -r win-x64 "${single[@]}" -o "$out/_win"
cp "$out/_win/CocTimeManager.Desktop.exe" "$out/CocUpgradeTimer-$ver-windows-x64.exe"

echo "== Linux"
"$dotnet" publish CocTimeManager.Desktop -r linux-x64 "${single[@]}" -o "$out/_linux"
pkg="$out/CocUpgradeTimer-$ver-linux-x64"
mkdir -p "$pkg"
cp "$out/_linux/CocTimeManager.Desktop" "$pkg/coc-upgrade-timer"
cp packaging/linux/coc-upgrade-timer.desktop packaging/linux/install.sh packaging/linux/uninstall.sh "$pkg/"
magick CocTimeManager/icons/hammer.png -background none -resize 256x256 -gravity center -extent 256x256 "$pkg/coc-upgrade-timer.png"
chmod +x "$pkg/coc-upgrade-timer" "$pkg/install.sh" "$pkg/uninstall.sh"
tar -C "$out" -czf "$pkg.tar.gz" "$(basename "$pkg")"

echo "== Android"
"$android_dotnet" build CocTimeManager.Android -c Release \
    -p:ApplicationDisplayVersion="$ver" -p:ApplicationVersion="$(echo "$ver" | awk -F. '{print $1*10000+$2*100+$3}')"
cp CocTimeManager.Android/bin/Release/net10.0-android/com.tmk.coctimemanager-Signed.apk "$out/CocUpgradeTimer-$ver-android.apk"

rm -rf "$out/_win" "$out/_linux" "$pkg"
( cd "$out" && sha256sum CocUpgradeTimer-* > SHA256SUMS.txt )
ls -lh "$out"
