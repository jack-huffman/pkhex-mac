#!/bin/zsh
# Update the upstream PKHeX submodule to the latest master, re-sync sprites,
# and rebuild. If the build breaks, upstream changed an API our UI uses —
# fix PKHeX.Mac, then commit.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

echo "==> Fetching latest upstream PKHeX master..."
git -C upstream/PKHeX fetch --depth 1 origin master
OLD=$(git -C upstream/PKHeX rev-parse --short HEAD)
git -C upstream/PKHeX checkout -q FETCH_HEAD
NEW=$(git -C upstream/PKHeX rev-parse --short HEAD)

if [[ "$OLD" == "$NEW" ]]; then
  echo "Already up to date ($NEW)."
  exit 0
fi
echo "==> Updated $OLD -> $NEW"
grep -m1 '<Version>' upstream/PKHeX/Directory.Build.props || true

echo "==> Syncing sprites..."
./scripts/sync-sprites.sh

echo "==> Building..."
if dotnet build PKHeX.Mac -c Debug; then
  echo ""
  echo "Build OK. To record the update:"
  echo "  git add upstream/PKHeX PKHeX.Mac/Assets && git commit -m \"Update upstream PKHeX to $NEW\""
else
  echo ""
  echo "BUILD FAILED — upstream changed an API used by PKHeX.Mac." >&2
  echo "Fix the UI code, or roll back with: git -C upstream/PKHeX checkout $OLD" >&2
  exit 1
fi
