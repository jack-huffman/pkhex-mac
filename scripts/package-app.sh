#!/bin/zsh
# Build a distributable PKHeX.app bundle (Apple Silicon, self-contained —
# no .NET install needed on the target machine). Output: dist/PKHeX.app
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

APP="dist/PKHeX.app"
PUBLISH="PKHeX.Mac/bin/Release/net10.0/osx-arm64/publish"

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

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>PKHeX</string>
    <key>CFBundleDisplayName</key>       <string>PKHeX</string>
    <key>CFBundleExecutable</key>        <string>PKHeX.Mac</string>
    <key>CFBundleIdentifier</key>        <string>dev.jackhuffman.pkhex-mac</string>
    <key>CFBundleVersion</key>           <string>1.0</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
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
echo "Launch with: open \"$APP\"  (or double-click in Finder)"
