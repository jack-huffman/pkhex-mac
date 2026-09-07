#!/bin/zsh
# Build a distributable PKHeX.app bundle (Apple Silicon, self-contained —
# no .NET install needed on the target machine). Output: dist/PKHeX.app
#
# Pass --install to also replace /Applications/PKHeX.app with it. Nothing else in
# the build chain touches /Applications, so a green build and passing tests never
# reach the app you actually launch unless you do this.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

INSTALL=0
for arg in "$@"; do
  case "$arg" in
    --install) INSTALL=1 ;;
    *) echo "usage: $0 [--install]" >&2; exit 2 ;;
  esac
done

APP="dist/PKHeX.app"
PUBLISH="PKHeX.Mac/bin/Release/net10.0/osx-arm64/publish"

# The bundle's version is the app's own (Directory.Build.props); the engine version is
# recorded alongside it so a bug report can say which PKHeX.Core it was built against.
APP_VERSION=$(dotnet msbuild PKHeX.Mac/PKHeX.Mac.csproj -getProperty:Version -nologo | tr -d '[:space:]')
CORE_VERSION=$(sed -nE 's/.*<Version>([^<]+)<\/Version>.*/\1/p' upstream/PKHeX/Directory.Build.props | head -1)
echo "==> PKHeX for Mac ${APP_VERSION} (PKHeX.Core ${CORE_VERSION})"

echo "==> Publishing (Release, osx-arm64, self-contained)..."
dotnet publish PKHeX.Mac -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none | tail -2

echo "==> Assembling ${APP}..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/"* "$APP/Contents/MacOS/"

# Bundle the optional high-res sprites (fetch with scripts/fetch-hires-sprites.sh).
if [[ -d PKHeX.Mac/Assets/hires ]]; then
  echo "==> Bundling high-res sprites..."
  cp -R PKHeX.Mac/Assets/hires "$APP/Contents/MacOS/hires"
fi

# App icon from the PKHeX icon.png
if [[ -f icon.png ]]; then
  ICONSET=$(mktemp -d)/AppIcon.iconset
  mkdir -p "$ICONSET"
  for size in 16 32 64 128 256 512; do
    sips -z $size $size icon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    sips -z $((size*2)) $((size*2)) icon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>PKHeX</string>
    <key>CFBundleDisplayName</key>       <string>PKHeX</string>
    <key>CFBundleExecutable</key>        <string>PKHeX.Mac</string>
    <key>CFBundleIdentifier</key>        <string>dev.jackhuffman.pkhex-mac</string>
    <key>CFBundleVersion</key>           <string>${APP_VERSION}</string>
    <key>CFBundleShortVersionString</key><string>${APP_VERSION}</string>
    <key>PKHeXCoreVersion</key>          <string>${CORE_VERSION}</string>
    <key>LSApplicationCategoryType</key> <string>public.app-category.utilities</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleIconFile</key>          <string>AppIcon</string>
    <key>NSHighResolutionCapable</key>   <true/>
    <key>LSMinimumSystemVersion</key>    <string>12.0</string>
    <key>NSHumanReadableCopyright</key>  <string>GPL-3.0 — UI © Jack Huffman, engine © Kaphotics (PKHeX)</string>
</dict>
</plist>
PLIST

echo "==> Ad-hoc code signing..."
codesign --force --deep -s - "$APP"

echo ""
echo "Done: $APP"
du -sh "$APP"

if (( INSTALL )); then
  DEST="/Applications/PKHeX.app"
  # Replacing a running bundle leaves the launched copy in a half-swapped state.
  if pgrep -f "$DEST/Contents/MacOS/PKHeX.Mac" >/dev/null 2>&1; then
    echo "" >&2
    echo "PKHeX is running — quit it first, then re-run with --install." >&2
    exit 1
  fi
  echo "==> Installing to ${DEST}..."
  rm -rf "$DEST"
  ditto "$APP" "$DEST"
  echo "Installed: $DEST"
else
  echo "Launch with: open \"$APP\"  (or double-click in Finder)"
  echo "Install to /Applications with: $0 --install"
fi
