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

if [[ ! -s "$urls_file" ]]; then
  echo "Already complete: $(ls "$DST" | grep -c '\.png') normal, $(ls "$DST/shiny" | grep -c '\.png') shiny."
  rm -f "$urls_file"
  exit 0
fi

echo "Fetching $(grep -c '^url' "$urls_file") files (parallel, resumable)..."
# -f: skip 404s (a few ids may not exist yet); --parallel for speed.
curl -sf --parallel --parallel-max 12 --retry 2 --config "$urls_file" || true
rm -f "$urls_file"

# Remove empty files from failed downloads so re-runs retry them.
find "$DST" -name "*.png" -size 0 -delete

echo "Done: $(ls "$DST" | grep -c '\.png') normal, $(ls "$DST/shiny" | grep -c '\.png') shiny sprites in $DST"