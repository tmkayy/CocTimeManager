#!/usr/bin/env python3
"""Regenerates CocTimeManager/Data/game_names.json, the id -> name table used to label
upgrades imported from the in-game "Export village data" JSON.

Source: coc.py's static game data (MIT licence), https://github.com/mathsman5133/coc.py
Run it after a game update adds new buildings/troops/pets:  python3 scripts/update-game-names.py
"""
import json
import pathlib
import urllib.request

URL = "https://raw.githubusercontent.com/mathsman5133/coc.py/master/coc/static/static_data.json"
CATEGORIES = ["buildings", "traps", "troops", "guardians", "spells", "heroes", "pets", "equipment", "helpers"]
OUT = pathlib.Path(__file__).resolve().parent.parent / "CocTimeManager" / "Data" / "game_names.json"

with urllib.request.urlopen(URL, timeout=60) as resp:
    data = json.load(resp)

names = {}
for cat in CATEGORIES:
    for item in data.get(cat, []):
        if "_id" in item and item.get("name"):
            names[str(item["_id"])] = item["name"]

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(dict(sorted(names.items(), key=lambda kv: int(kv[0]))), indent=0, ensure_ascii=False) + "\n",
               encoding="utf-8")
print(f"Wrote {len(names)} names to {OUT}")
