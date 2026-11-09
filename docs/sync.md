# Syncing devices

Keep your list the same on two devices, with no account or server.

## Same Wi-Fi

Press **Show code** on one device; a 4-digit code appears. Press **Enter code** on the other and type it. Both lists are
merged both ways in a couple of seconds, directly over your network.

If it can't find the other device, swap which one shows the code. The device *showing* the code must accept incoming
connections on port 45871, so a desktop firewall can block it (Linux with ufw: `sudo ufw allow 45871`, Windows asks the
first time). The device *entering* the code works through firewalls.

## Staying in sync

A code sync on the same Wi-Fi also **pairs** the two devices, and from then on they sync by themselves:

- **Phone to computer:** every change on the phone is sent straight to the computer, including an import from the
  home screen widget. Pop a potion, export, import on the phone, and the computer has it too.
- **Computer to phone:** the phone fetches the computer's changes whenever you open the app.
- The computer has to be running the app (it can stay in the system tray, see *Start at login* under *Settings*), on
  the same Wi-Fi, with port 45871 allowed through its firewall. Phones can't receive while the app is closed, which
  is why the phone fetches when it opens.
- The *Sync* section shows who you're paired with and when you last synced. **Sync now** syncs straight away, and
  **Forget device** unpairs. Syncing with a code again pairs with that device instead.

Paired devices only accept syncs signed with a key they agreed on when pairing; the key itself never goes over the
network.

## Anywhere else

Press **Copy code**, send the long code to your other device yourself (messaging app, notes, email) and press
**Paste code** there.

## How merging works

Merging never loses anything: upgrades you deleted stay deleted, the newest village import wins, and an upgrade that
already notified on one device won't notify again.
