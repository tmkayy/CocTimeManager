# Importing from the game

1. In Clash of Clans go to *Settings → More Settings → Export village data*. This copies JSON to the clipboard.
2. In the app, press **Import village**. On Android you can tap **Import** on the home screen widget instead.

Every running building/hero/trap upgrade, lab research, pet upgrade and Builder Base upgrade is added with its exact
finish time.

Import again any time to sync: entries keep their notifications, finished or cancelled ones are removed, and upgrades
you added by hand are left alone. Progress bars for newly imported items count from the export, because the export only
says how much time is left.

## Active boosts

Boosts active when you export are taken into account, using the time left listed in the export:

- A **Builder Potion** gives home-village builder upgrades 10× speed.
- A **Research Potion** gives lab research 24× speed.
- Pets and the Builder Base aren't affected by either.
- A **Builder's Apprentice** or **Lab Assistant** adds its level on top for the rest of its session, e.g. a level 8
  Apprentice during a Builder Potion is 18×.
- If you've set a helper to reapply itself, its future sessions (every 23 hours) are counted too.

Builder Bites and Study Soup aren't read from exports yet.
