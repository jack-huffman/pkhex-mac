#!/bin/zsh
# Fetch battle metadata for every move (power, accuracy, damage class) from PokeAPI
# into PKHeX.Mac/Assets/movedata.json.
#
# PKHeX.Core only stores each move's type and PP — it is a legality engine and does
# not need battle numbers — so this fills the gap for the editor's move pickers.
# The result is small (~60 KB) and IS committed, unlike the sprite cache.
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="PKHeX.Mac/Assets/movedata.json"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "==> Listing moves..."
curl -sf "https://pokeapi.co/api/v2/move?limit=100000" -o "$TMP/list.json"

python3 - "$TMP" <<'PY'
import json, sys, os
tmp = sys.argv[1]
data = json.load(open(f"{tmp}/list.json"))
urls = []
for r in data["results"]:
    mid = int(r["url"].rstrip("/").split("/")[-1])
    if mid > 10000:      # PokeAPI's shadow/special entries; not real move ids
        continue
    urls.append((mid, r["url"]))
with open(f"{tmp}/urls.txt", "w") as f:
    for mid, url in urls:
        f.write(f'url = "{url}"\noutput = "{tmp}/m/{mid}.json"\n')
os.makedirs(f"{tmp}/m", exist_ok=True)
print(f"{len(urls)} moves to fetch")
PY

echo "==> Fetching move details (parallel)..."
curl -sf --parallel --parallel-max 16 --retry 2 --config "$TMP/urls.txt" || true

python3 - "$TMP" "$OUT" <<'PY'
import json, os, sys
tmp, out = sys.argv[1], sys.argv[2]
result = {}
for name in os.listdir(f"{tmp}/m"):
    if not name.endswith(".json"):
        continue
    try:
        d = json.load(open(f"{tmp}/m/{name}"))
    except Exception:
        continue
    mid = str(d["id"])
    # Short English description, preferring the concise "short_effect".
    desc = ""
    for e in d.get("effect_entries", []):
        if e.get("language", {}).get("name") == "en":
            desc = e.get("short_effect", "") or ""
            break
    result[mid] = {
        "p": d.get("power"),                                  # null for status moves
        "a": d.get("accuracy"),                               # null = never misses
        "c": (d.get("damage_class") or {}).get("name", ""),   # physical / special / status
        "d": desc.replace("$effect_chance", "the listed").strip(),
    }
os.makedirs(os.path.dirname(out), exist_ok=True)
json.dump(result, open(out, "w"), separators=(",", ":"), ensure_ascii=False)
print(f"wrote {len(result)} moves -> {out} ({os.path.getsize(out)//1024} KB)")
PY
