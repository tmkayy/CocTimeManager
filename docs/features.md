# Features

## Calculator

- Upgrade time input in days / hours / minutes
- **Builder Potion**: enter how long it stays active (stacked potions = more hours). Builders work 10× faster while it's active.
- **Builder Bite**: enter how long bites stay active (1 bite = 1 hour). Builders work 2× faster while it's active.
- **Gold Pass**: default 20 %, editable for your tier
- **Hammer Jam / Summer Jam**: one slot, since they never run at the same time (default 50 %, editable)
- **Inactivity Boost**: editable percentage
- Result: local time now, finish date/time, real duration and total reduction %, with a per-step breakdown on the ⓘ badge

See [How the maths works](calculations.md) for how these combine.

## Tracked upgrades

A list of your upgrades with a green progress bar and countdown, saved between sessions. Add them by hand with
**Start & track**, or all at once by [importing from the game](importing.md).

Untick **Builder Base upgrades** above the list to hide those timers and silence their notifications. They're still
imported and synced, so ticking it shows them again.

## Notifications

When an upgrade finishes you get a notification sound and a system notification.

- **Desktop:** while the app is running (or the next time you open it, if it finished while closed).
  Closing the window keeps it running in the **system tray**; use **Quit** in the tray menu to exit.
  Tick **Start at login** (in the app or the tray menu) and it starts hidden in the tray when you log in,
  so you get notified even after a reboot without opening it.
- **Android:** even when the app is closed (scheduled alarm, survives reboots).

## Android home screen widget

Running upgrades with live countdowns and finish times, soonest first. Long-press your home screen, choose *Widgets*
and find *Upgrade timers*; resize it to fit more rows. It follows the Builder Base toggle and updates whenever the list
changes, such as after an import. Android versions that allow lock screen widgets can place it there too, but most
phones don't.

## In-app updates

The app checks for a new release on startup (or press *Settings → Check for updates*) and shows an **Update** button.
The download is verified against the release's `SHA256SUMS.txt`. Windows and Linux replace themselves and restart; on
Android the system installer opens (the first time, Android asks you to allow installs from this app). Builds run from
source don't update themselves.

## Where your data is saved

Tracked upgrades are saved as JSON (times in UTC) in the user config folder:
`~/.config/CocTimeManager/upgrades.json` on Linux, `%APPDATA%\CocTimeManager\upgrades.json` on Windows.
Recent deletions are kept in `deleted.json` next to it for [syncing](sync.md), and app settings in `settings.json`.
