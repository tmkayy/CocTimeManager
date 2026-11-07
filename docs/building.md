# Building from source

## Prerequisites

- [.NET SDK 9.0 or newer](https://dotnet.microsoft.com/download) (the desktop app targets `net9.0`)
- Git

```bash
git clone https://github.com/tmkayy/CocTimeManager.git
cd CocTimeManager
```

## Windows / Linux / macOS

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

## Android

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
> Android only installs an update signed with the same key as the installed app. A machine with no debug key
> generates a new one, so its APK won't install over a release: uninstall first, which loses the saved timers.

## Building release files

`scripts/build-release.sh <version>` builds the Windows `.exe`, the Linux `.tar.gz` (with launcher entry and installer)
and the Android `.apk` into `release/`. It needs ImageMagick (`magick`) for the Linux icon, and a `dotnet` with the
Android workload (set `ANDROID_DOTNET`, default `~/.dotnet/dotnet`).

## Updating game names

Imported items are named using `CocTimeManager/Data/game_names.json`. When a game update adds new items (they show up
as e.g. `Pet #73000017`), regenerate it from [coc.py](https://github.com/mathsman5133/coc.py)'s game data:

```bash
python3 scripts/update-game-names.py
```

## Project layout

```
CocTimeManager/            shared UI and logic (Avalonia), icons, fonts, sound
  Data/game_names.json     game id -> name table for village imports
  Models/                  upgrade time calculator, tracked-upgrade storage, village export import, sync codes
  Services/                notifications, start at login, Wi-Fi sync with a 4-digit code, in-app updates
  Views/                   MainView (the app UI) and the desktop MainWindow
  Styles/                  colours, text styles, progress bar, ⓘ badges
CocTimeManager.Desktop/    desktop entry point, sound/notification, start at login, single instance, self-update
CocTimeManager.Android/    Android entry point, scheduled notifications, boot receiver, home screen widget, APK update installer
docs/                      this documentation
packaging/linux/           .desktop launcher entry, install.sh / uninstall.sh
scripts/build-release.sh   builds all release files
scripts/update-game-names.py  regenerates Data/game_names.json
```
