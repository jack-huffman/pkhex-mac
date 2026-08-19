#!/bin/zsh
# Sync sprite assets from the upstream PKHeX submodule into the Mac app,
# renaming folders to URL-safe names used by avares:// URIs.
set -euo pipefail
cd "$(dirname "$0")/.."

SRC="upstream/PKHeX/PKHeX.Drawing.PokeSprite/Resources/img"
DST="PKHeX.Mac/Assets/img"

if [[ ! -d "$SRC" ]]; then
  echo "error: $SRC not found — run 'git submodule update --init' first" >&2
  exit 1
fi

mkdir -p "$DST"

typeset -A MAP
MAP=(
  "Big Pokemon Sprites"        big
  "Big Shiny Sprites"          big-shiny
  "Artwork Pokemon Sprites"    artwork
  "Artwork Shiny Sprites"      artwork-shiny
  "Big Items"                  items
  "Artwork Items"              items-artwork
  "Legends Arceus Sprites"     la
  "Legends Arceus Shiny Sprites" la-shiny
  "Pokemon Sprite Overlays"    overlays
  "Status"                     status
  "accents"                    accents
  "ball"                       ball
)

for src_name dst_name in "${(@kv)MAP}"; do
  if [[ -d "$SRC/$src_name" ]]; then
    rsync -a --delete "$SRC/$src_name/" "$DST/$dst_name/"
    echo "synced: $src_name -> $dst_name ($(ls "$DST/$dst_name" | wc -l | tr -d ' ') files)"
  else
    echo "warning: upstream folder not found: $src_name (renamed upstream?)" >&2
  fi
done

# Loose top-level images (hint/valid/warn markers).
rsync -a --include='*.png' --exclude='*/' "$SRC/" "$DST/"
echo "done: $(find "$DST" -type f | wc -l | tr -d ' ') total files"
