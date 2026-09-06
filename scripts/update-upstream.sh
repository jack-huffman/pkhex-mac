#!/bin/zsh
# Update the upstream PKHeX submodule to the latest master, re-sync sprites,
# rebuild, and run the tests.
#
# What this catches:
#   - C# API breaks (upstream renamed/removed something PKHeX.Mac calls)
#   - XAML binding breaks — compiled bindings are on, so a stale binding path
#     is a build error (AVLN2000), not a silent runtime failure
#   - behavioural regressions the test suite covers
#
# What it CANNOT catch, and why the changelog is printed for you to read:
#   - semantics changing under an unchanged signature. Upstream's 26.08.26
#     "Fix slot write/batch regressions" is the type case: EntityImportSettings
#     kept its shape while what it means for a slot write changed.
#   - new upstream features this UI simply does not surface yet.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

OLD=$(git -C upstream/PKHeX rev-parse HEAD)

echo "==> Fetching latest upstream PKHeX master..."
# Depth 50 rather than 1: enough history to print a changelog range below.
# The submodule is a shallow clone, so a depth-1 fetch leaves nothing to diff against.
git -C upstream/PKHeX fetch --depth 50 --tags origin master
git -C upstream/PKHeX checkout -q FETCH_HEAD
NEW=$(git -C upstream/PKHeX rev-parse HEAD)

if [[ "$OLD" == "$NEW" ]]; then
  echo "Already up to date ($(git -C upstream/PKHeX rev-parse --short HEAD))."
  exit 0
fi

echo "==> Updated $(git -C upstream/PKHeX rev-parse --short $OLD) -> $(git -C upstream/PKHeX rev-parse --short $NEW)"
grep -m1 '<Version>' upstream/PKHeX/Directory.Build.props || true

echo ""
echo "==> Upstream changes you are pulling in:"
# Guard: a shallow clone may not reach OLD, in which case there is no range to show.
if git -C upstream/PKHeX merge-base --is-ancestor "$OLD" "$NEW" 2>/dev/null; then
  git -C upstream/PKHeX log --oneline --no-decorate "$OLD..$NEW"
else
  echo "  (history too shallow to list; see the release notes)"
fi
TAG=$(git -C upstream/PKHeX describe --tags --abbrev=0 "$NEW" 2>/dev/null || true)
if [[ -n "$TAG" ]]; then
  echo ""
  echo "  Release notes: https://github.com/kwsch/PKHeX/releases/tag/$TAG"
  echo "  Read them. A green build does not prove upstream still means what it used to."
fi

echo ""
echo "==> Syncing sprites..."
./scripts/sync-sprites.sh

echo "==> Building..."
if ! dotnet build PKHeX.Mac -c Debug; then
  echo ""
  echo "BUILD FAILED — upstream changed an API or a binding target used by PKHeX.Mac." >&2
  echo "Fix the UI code, or roll back with: git -C upstream/PKHeX checkout $OLD" >&2
  exit 1
fi

echo "==> Testing..."
if ! dotnet test PKHeX.Mac.Tests -c Debug --nologo; then
  echo ""
  echo "TESTS FAILED — it compiles against upstream but no longer behaves." >&2
  echo "Roll back with: git -C upstream/PKHeX checkout $OLD" >&2
  exit 1
fi

echo ""
echo "Build and tests OK. To record the update:"
echo "  git add upstream/PKHeX PKHeX.Mac/Assets && git commit -m \"Update upstream PKHeX to $(git -C upstream/PKHeX rev-parse --short $NEW)\""
echo ""
echo "Then, if you want the change in the app you actually launch:"
echo "  ./scripts/package-app.sh --install"
