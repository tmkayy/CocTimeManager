#!/usr/bin/env bash
# Removes CoC Upgrade Timer (use --system if it was installed with --system).
# Your tracked upgrades in ~/.config/CocTimeManager are kept.
set -euo pipefail
if [[ "${1:-}" == "--system" ]]; then
    app_dir=/opt/coc-upgrade-timer; bin_dir=/usr/local/bin; share=/usr/share
else
    app_dir="${XDG_DATA_HOME:-$HOME/.local/share}/coc-upgrade-timer"; bin_dir="$HOME/.local/bin"
    share="${XDG_DATA_HOME:-$HOME/.local/share}"
fi
rm -rf "$app_dir"
rm -f "$bin_dir/coc-upgrade-timer" "$share/applications/coc-upgrade-timer.desktop" \
      "$share/icons/hicolor/256x256/apps/coc-upgrade-timer.png"
# Start-at-login entry written by the app (always per user).
rm -f "${XDG_CONFIG_HOME:-$HOME/.config}/autostart/coc-upgrade-timer.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database -q "$share/applications" || true
echo "Uninstalled."
