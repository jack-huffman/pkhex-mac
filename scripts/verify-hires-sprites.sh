#!/bin/zsh
# Verify the high-res sprite set is complete:
#  1. every species 1..1025 has a normal + shiny render
#  2. form renders exist for the alternate forms that matter
#  3. every PNG decodes (no truncated downloads)
set -euo pipefail
cd "$(dirname "$0")/.."
DST="PKHeX.Mac/Assets/hires"

python3 - "$DST" <<'PY'
import json, os, struct, sys

dst = sys.argv[1]
ok = True

def is_png(path):
    try:
        with open(path, "rb") as f:
            return f.read(8) == b"\x89PNG\r\n\x1a\n" and os.path.getsize(path) > 100
    except OSError:
        return False

# 1. Species coverage 1..1025, normal + shiny.
for sub, label in (("", "normal"), ("shiny", "shiny")):
    missing = [i for i in range(1, 1026) if not os.path.isfile(os.path.join(dst, sub, f"{i}.png"))]
    if missing:
        ok = False
        print(f"MISSING {label}: {len(missing)} species -> {missing[:20]}{'...' if len(missing) > 20 else ''}")
    else:
        print(f"OK: all 1025 {label} species renders present")

# 2. Important alternate forms resolvable by name and present on disk.
forms = json.load(open(os.path.join(dst, "forms.json")))
important = [
    "typhlosion-hisui", "ursaluna-bloodmoon", "growlithe-hisui", "arcanine-hisui",
    "zorua-hisui", "zoroark-hisui", "samurott-hisui", "decidueye-hisui",
    "lilligant-hisui", "sliggoo-hisui", "goodra-hisui", "avalugg-hisui",
    "braviary-hisui", "sneasel-hisui", "qwilfish-hisui", "basculegion-female",
    "raichu-alola", "vulpix-alola", "ninetales-alola", "sandshrew-alola",
    "meowth-galar", "ponyta-galar", "slowpoke-galar", "weezing-galar",
    "wooper-paldea", "tauros-paldea-combat-breed",
    "rotom-wash", "rotom-heat", "rotom-frost", "rotom-fan", "rotom-mow",
    "giratina-origin", "dialga-origin", "palkia-origin",
    "tornadus-therian", "thundurus-therian", "landorus-therian", "enamorus-therian",
    "ogerpon-wellspring-mask", "ogerpon-hearthflame-mask", "ogerpon-cornerstone-mask",
    "deoxys-attack", "deoxys-defense", "deoxys-speed",
    "lycanroc-midnight", "lycanroc-dusk", "toxtricity-low-key",
    "indeedee-female", "oinkologne-female", "meowstic-female",
    "urshifu-rapid-strike", "calyrex-ice", "calyrex-shadow",
    "zacian-crowned", "zamazenta-crowned", "eternatus-eternamax",
    "terapagos-terastal", "palafin-hero", "gimmighoul-roaming",
]
miss_map, miss_file = [], []
for name in important:
    if name not in forms:
        miss_map.append(name)
        continue
    fid = forms[name]
    if not os.path.isfile(os.path.join(dst, "forms", f"{fid}.png")):
        miss_file.append(f"{name}({fid})")
if miss_map:
    ok = False
    print(f"NOT IN MAP: {miss_map}")
if miss_file:
    print(f"NO RENDER (PokeAPI has none): {miss_file}")
if not miss_map and not miss_file:
    print(f"OK: all {len(important)} important alternate forms mapped and present")

# 3. Every PNG decodes (header check).
bad = []
for root, _, files in os.walk(dst):
    for f in files:
        if f.endswith(".png") and not is_png(os.path.join(root, f)):
            bad.append(os.path.join(root, f))
if bad:
    ok = False
    print(f"CORRUPT/TRUNCATED: {bad}")
else:
    total = sum(len([f for f in fs if f.endswith('.png')]) for _, _, fs in os.walk(dst))
    print(f"OK: all {total} PNG files have valid headers")

form_count = len([f for f in os.listdir(os.path.join(dst, 'forms')) if f.endswith('.png')])
form_shiny = len([f for f in os.listdir(os.path.join(dst, 'forms', 'shiny')) if f.endswith('.png')])
print(f"Form renders on disk: {form_count} normal, {form_shiny} shiny")
sys.exit(0 if ok else 1)
PY
