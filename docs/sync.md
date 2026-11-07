# Syncing devices

Keep your list the same on two devices, with no account or server.

## Same Wi-Fi

Press **Show code** on one device; a 4-digit code appears. Press **Enter code** on the other and type it. Both lists are
merged both ways in a couple of seconds, directly over your network.

If it can't find the other device, swap which one shows the code. The device *showing* the code must accept incoming
connections on port 45871, so a desktop firewall can block it (Linux with ufw: `sudo ufw allow 45871`, Windows asks the
first time). The device *entering* the code works through firewalls.

## Anywhere else

Press **Copy code**, send the long code to your other device yourself (messaging app, notes, email) and press
**Paste code** there.

## How merging works

Merging never loses anything: upgrades you deleted stay deleted, the newest village import wins, and an upgrade that
already notified on one device won't notify again.
