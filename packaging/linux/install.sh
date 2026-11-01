#!/usr/bin/env bash
# Installs CoC Upgrade Timer for the current user and adds it to the app launcher.
#   ./install.sh            install to ~/.local (no root needed)
#   sudo ./install.sh --system   install for all users (/opt + /usr/share)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--system" ]]; then
    app_dir=/opt/coc-upgrade-timer
    bin_dir=/usr/local/bin
    share=/usr/share
else
    app_dir="${XDG_DATA_HOME:-$HOME/.local/share}/coc-upgrade-timer"
    bin_dir="$HOME/.local/bin"
    share="${XDG_DATA_HOME:-$HOME/.local/share}"
fi

mkdir -p "$app_dir" "$bin_dir" "$share/applications" "$share/icons/hicolor/256x256/apps"

install -m 755 "$here/coc-upgrade-timer" "$app_dir/coc-upgrade-timer"
install -m 644 "$here/coc-upgrade-timer.png" "$share/icons/hicolor/256x256/apps/coc-upgrade-timer.png"
ln -sf "$app_dir/coc-upgrade-timer" "$bin_dir/coc-upgrade-timer"
sed "s|@EXEC@|$app_dir/coc-upgrade-timer|" "$here/coc-upgrade-timer.desktop" \
    > "$share/applications/coc-upgrade-timer.desktop"
chmod 644 "$share/applications/coc-upgrade-timer.desktop"

command -v update-desktop-database >/dev/null && update-desktop-database -q "$share/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$share/icons/hicolor" 2>/dev/null || true

echo "Installed. Find \"CoC Upgrade Timer\" in your app launcher, or run: coc-upgrade-timer"
echo "Notifications use notify-send and ffplay/mpg123/mpv/pw-play/paplay if installed."
