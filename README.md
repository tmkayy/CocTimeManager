# CoC Upgrade Timer

**Preface: All of this is vibe coded using Claude Code.**

A small Clash of Clans upgrade calculator and tracker built with [Avalonia UI](https://avaloniaui.net/) (.NET).
Runs on Windows, Linux, macOS and Android.

Enter an upgrade's full time, tick the boosts you have, and it tells you **how long the upgrade will really take**,
**when it will be done** (in your system's local time and date format) and the **total % time reduction**.
Start tracking it and you get a progress bar, a live countdown, and a sound + system notification when it finishes.

## Features

- Upgrade time input in days / hours / minutes
- **Builder Potion** – enter how long it stays active (stacked potions = more hours). Builders work 10× faster while it's active.
- **Builder Bite** – enter how long bites stay active (1 bite = 1 hour). Builders work 2× faster while it's active.
- **Gold Pass** – default 20 %, editable for your tier
- **Hammer Jam / Summer Jam** – one slot, since they never run at the same time (default 50 %, editable)
- **Inactivity Boost** – editable percentage
- Result: local time now, finish date/time, real duration, total reduction %, with a per-step breakdown on the ⓘ badge
- Tracked upgrades list with a green progress bar and countdown, saved between sessions
- **Import from the game**: in Clash of Clans go to *Settings → More Settings → Export village data* (copies JSON to
  the clipboard), then press **Import village**. Every running building/hero/trap upgrade, lab research, pet upgrade
  and Builder Base upgrade is added with its exact finish time. Import again any time to sync: entries keep their
  notifications, finished or cancelled ones are removed, and upgrades you added by hand are left alone.
  Progress bars for newly imported items count from the export (the export only says how much time is left).
- When an upgrade finishes: notification sound + system notification
  - **Desktop:** while the app is running (or the next time you open it, if it finished while closed).
    Closing the window keeps it running in the **system tray**; use **Quit** in the tray menu to exit.
    Tick **Start at login** (in the app or the tray menu) and it starts hidden in the tray when you log in,
    so you get notified even after a reboot without opening it.
  - **Android:** even when the app is closed (scheduled alarm, survives reboots)

### How the maths works

- Percentage reductions stack **multiplicatively**: Gold Pass 20 % + Hammer Jam 50 % → `0.8 × 0.5 = 0.4` of the time → **60 %** off.
- The Builder Potion is applied after that: every real hour of potion does 10 hours of work, so 1 potion saves 9 hours
  (less if the upgrade finishes before the potion runs out — the breakdown shows any wasted potion time).
- Builder Bites (2×) **don't stack with a Builder Potion**: since the February 2026 update, a bite adds nothing
  while a potion is active. So bites are counted as used right after the potion ends (what the game recommends):
  10× during the potion, then 2× during the bites, then normal speed.
  Example: 24 h upgrade + 1 potion + 1 bite → 10 h of work in the potion hour, 2 h in the bite hour, 12 h left → **14 h**.

## Download

Ready-made builds are on the [Releases page](https://github.com/tmkayy/CocTimeManager/releases/latest). No .NET install needed.

| Platform | File | How to install |
|---|---|---|
| Windows (64-bit) | `CocUpgradeTimer-<version>-windows-x64.exe` | Just run it. Windows SmartScreen may warn because the file isn't code-signed: click **More info → Run anyway**. |
| Linux (x64) | `CocUpgradeTimer-<version>-linux-x64.tar.gz` | Extract it and run `./install.sh`. This adds **CoC Upgrade Timer** to your app launcher (with icon) and a `coc-upgrade-timer` command. Use `sudo ./install.sh --system` to install for all users, and `./uninstall.sh` to remove it (also removes the start-at-login entry). |
| Android 8.0+ | `CocUpgradeTimer-<version>-android.apk` | Open the APK on your phone and allow installing from unknown sources. Allow notifications on first launch. |

```bash
# Linux example
tar -xzf CocUpgradeTimer-1.1.0-linux-x64.tar.gz
cd CocUpgradeTimer-1.1.0-linux-x64
./install.sh
```

`SHA256SUMS.txt` on the release lets you verify the downloads (`sha256sum -c SHA256SUMS.txt`).

## Build from source

### Prerequisites

- [.NET SDK 9.0 or newer](https://dotnet.microsoft.com/download) (the desktop app targets `net9.0`)
- Git

```bash
git clone https://github.com/tmkayy/CocTimeManager.git
cd CocTimeManager
```

### Windows / Linux / macOS

Run it directly:

```bash
dotnet run --project CocTimeManager.Desktop -c Release
```

Or publish a standalone build you can copy anywhere (no .NET install needed on the target machine).
Pick the runtime for your system: `win-x64`, `linux-x64`, `osx-arm64` (Apple Silicon) or `osx-x64` (Intel Mac).

```bash
dotnet publish CocTimeManager.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Then start `publish/CocTimeManager.Desktop` (`CocTimeManager.Desktop.exe` on Windows).

**Linux notification requirements:** the sound needs one of `ffplay`, `mpg123`, `mpv`, `pw-play` or `paplay`,
and the popup needs `notify-send` (package `libnotify`). Most desktops already have these.

**Windows / macOS:** nothing extra; Windows uses the built-in media API and toast notifications, macOS uses `afplay` and `osascript`.

### Android

Building the Android app needs the .NET Android workload, a JDK (17 or 21) and the Android SDK.

1. Install the workload:

   ```bash
   dotnet workload install android
   ```

   If your .NET is installed system-wide (e.g. from a Linux package manager) this needs `sudo`.
   Without root, install a private SDK into your home folder and use that `dotnet` instead:

   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir ~/.dotnet
   ~/.dotnet/dotnet workload install android
   ```

2. Install a JDK 21 (e.g. [Microsoft Build of OpenJDK](https://learn.microsoft.com/java/openjdk/download)).
   The project looks in `~/.local/share/jdk-21` by default; point elsewhere with `-p:JavaSdkDirectory=/path/to/jdk`.

3. Download the Android SDK packages (goes to `~/Android/Sdk` by default; override with `-p:AndroidSdkDirectory=...`):

   ```bash
   dotnet build CocTimeManager.Android -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=True
   ```

4. Build the APK:

   ```bash
   dotnet build CocTimeManager.Android -c Release
   ```

   The installable file is `CocTimeManager.Android/bin/Release/net10.0-android/com.tmk.coctimemanager-Signed.apk`.

5. Install it on your phone, either by copying the APK over and opening it (allow "install unknown apps"),
   or with USB debugging enabled:

   ```bash
   ~/Android/Sdk/platform-tools/adb install -r CocTimeManager.Android/bin/Release/net10.0-android/com.tmk.coctimemanager-Signed.apk
   ```

On first launch (Android 13+) allow notifications, otherwise finished upgrades can't alert you.
Requires Android 8.0 or newer.

> The APK is signed with a debug key, which is fine for installing on your own devices but not for the Play Store.

### Building release files

`scripts/build-release.sh <version>` builds the Windows `.exe`, the Linux `.tar.gz` (with launcher entry and installer)
and the Android `.apk` into `release/`. It needs ImageMagick (`magick`) for the Linux icon, and a `dotnet` with the
Android workload (set `ANDROID_DOTNET`, default `~/.dotnet/dotnet`).

### Updating game names

Imported items are named using `CocTimeManager/Data/game_names.json`. When a game update adds new items (they show up
as e.g. `Pet #73000017`), regenerate it from [coc.py](https://github.com/mathsman5133/coc.py)'s game data:

```bash
python3 scripts/update-game-names.py
```

## Project layout

```
CocTimeManager/            shared UI and logic (Avalonia), icons, fonts, sound
  Data/game_names.json     game id -> name table for village imports
  Models/                  upgrade time calculator, tracked-upgrade storage, village export import
  Services/                notification + start-at-login interfaces implemented per platform
  Views/                   MainView (the app UI) and the desktop MainWindow
  Styles/                  colours, text styles, progress bar, ⓘ badges
CocTimeManager.Desktop/    desktop entry point, sound/notification, start at login, single instance
CocTimeManager.Android/    Android entry point, scheduled notifications, boot receiver
packaging/linux/           .desktop launcher entry, install.sh / uninstall.sh
scripts/build-release.sh   builds all release files
scripts/update-game-names.py  regenerates Data/game_names.json
```

Tracked upgrades are saved as JSON (times in UTC) in the user config folder
(`~/.config/CocTimeManager/upgrades.json` on Linux, `%APPDATA%\CocTimeManager\upgrades.json` on Windows).
