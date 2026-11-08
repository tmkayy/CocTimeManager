# Install

<a class="md-button md-button--primary download-button" href="https://github.com/tmkayy/CocTimeManager/releases/latest">⬇ Download the latest release</a>

Ready-made builds are on the [Releases page](https://github.com/tmkayy/CocTimeManager/releases/latest). No .NET install needed.

| Platform | File | How to install |
|---|---|---|
| Windows (64-bit) | `CocUpgradeTimer-<version>-windows-x64.exe` | Just run it. Windows SmartScreen may warn because the file isn't code-signed: click **More info → Run anyway**. |
| Linux (x64) | `CocUpgradeTimer-<version>-linux-x64.tar.gz` | Extract it and run `./install.sh`. This adds **CoC Upgrade Timer** to your app launcher (with icon) and a `coc-upgrade-timer` command. Use `sudo ./install.sh --system` to install for all users, and `./uninstall.sh` to remove it (also removes the start-at-login entry). |
| Android 8.0+ | `CocUpgradeTimer-<version>-android.apk` | Open the APK on your phone and allow installing from unknown sources. Allow notifications on first launch. |

```bash
# Linux example
tar -xzf CocUpgradeTimer-1.4.1-linux-x64.tar.gz
cd CocUpgradeTimer-1.4.1-linux-x64
./install.sh
```

`SHA256SUMS.txt` on the release lets you verify the downloads (`sha256sum -c SHA256SUMS.txt`).

macOS has no ready-made build, but you can [build it from source](building.md).
