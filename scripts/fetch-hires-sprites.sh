#!/bin/zsh
# Fetch high-resolution (512x512) Pokémon HOME renders — normal + shiny —
# from the PokeAPI sprites repository into PKHeX.Mac/Assets/hires/.
#
# These are loaded from disk at runtime (NOT embedded in the binary) and are
# gitignored. Re-run any time; already-downloaded files are skipped.
# Personal use only — the images are © Nintendo/Game Freak; don't redistribute.
set -euo pipefail
cd "$(dirname "$0")/.."

BASE="https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home"
DST="PKHeX.Mac/Assets/hires"
MAX_SPECIES=${1:-1025}

mkdir -p "$DST/shiny"

urls_file=$(mktemp)
for id in $(seq 1 "$MAX_SPECIES"); do
  [[ -f "$DST/$id.png" ]]       || printf 'url = "%s/%s.png"\noutput = "%s/%s.png"\n' "$BASE" "$id" "$DST" "$id" >> "$urls_file"
  [[ -f "$DST/shiny/$id.png" ]] || printf 'url = "%s/shiny/%s.png"\noutput = "%s/shiny/%s.png"\n' "$BASE" "$id" "$DST" "$id" >> "$urls_file"
done

if [[ -s "$urls_file" ]]; then
  echo "Fetching $(grep -c '^url' "$urls_file") species files (parallel, resumable)..."
  # -f: skip 404s (a few ids may not exist yet); --parallel for speed.
  curl -sf --parallel --parallel-max 12 --retry 2 --config "$urls_file" || true
fi
rm -f "$urls_file"

# ---- Alternate-form renders (Hisuian, Bloodmoon, Rotom appliances, ...) ----
# PokeAPI hosts these under form-specific ids (>10000). Build a name->id map
# the app uses to resolve PKHeX form names, then fetch each form's renders.
echo "==> Building form id map from PokeAPI..."
mkdir -p "$DST/forms/shiny"
listing=$(mktemp)
curl -sf "https://pokeapi.co/api/v2/pokemon?limit=100000" -o "$listing"
python3 - "$listing" "$DST" <<'PY'
import json, sys
listing, dst = sys.argv[1], sys.argv[2]
data = json.load(open(listing))
m = {}
for r in data["results"]:
    pid = int(r["url"].rstrip("/").split("/")[-1])
    m[r["name"]] = pid
json.dump(m, open(f"{dst}/forms.json", "w"))
form_ids = sorted(i for i in m.values() if i > 10000)
open("/tmp/pkhex_form_ids.txt", "w").write("\n".join(map(str, form_ids)))
print(f"forms.json: {len(m)} names, {len(form_ids)} alternate forms")
PY
rm -f "$listing"

form_urls=$(mktemp)
while read -r id; do
  [[ -f "$DST/forms/$id.png" ]]       || printf 'url = "%s/%s.png"\noutput = "%s/forms/%s.png"\n' "$BASE" "$id" "$DST" "$id" >> "$form_urls"
  [[ -f "$DST/forms/shiny/$id.png" ]] || printf 'url = "%s/shiny/%s.png"\noutput = "%s/forms/shiny/%s.png"\n' "$BASE" "$id" "$DST" "$id" >> "$form_urls"
done < /tmp/pkhex_form_ids.txt

if [[ -s "$form_urls" ]]; then
  echo "Fetching $(grep -c '^url' "$form_urls") form files (404s are normal — not every form has a render)..."
  curl -sf --parallel --parallel-max 12 --retry 2 --config "$form_urls" || true
fi
rm -f "$form_urls" /tmp/pkhex_form_ids.txt

# Remove empty files from failed downloads so re-runs retry them.
find "$DST" -name "*.png" -size 0 -delete

echo "Done: $(ls "$DST" | grep -c '\.png') species, $(ls "$DST/shiny" | grep -c '\.png') shiny, $(ls "$DST/forms" | grep -c '\.png' || true) form, $(ls "$DST/forms/shiny" | grep -c '\.png' || true) shiny-form sprites"