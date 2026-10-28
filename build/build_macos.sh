#!/usr/bin/env bash
set -euo pipefail

APP_NAME="NovaVMS"
BUNDLE_ID="com.novavms.app"

# Expected repository layout:
# repository/
#   build/publish-macos.sh
#   src/NovaVMS.App/NovaVMS.App.csproj
#   src/NovaVMS.App/Assets/novavms.png
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_FILE="${1:-$ROOT_DIR/src/NovaVMS.App/NovaVMS.App.csproj}"
ICON_FILE="$ROOT_DIR/src/NovaVMS.App/Assets/novavms.png"
OUTPUT_DIR="dist"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Error: This script must run on macOS." >&2
  exit 1
fi

case "$(uname -m)" in
  arm64) RID="osx-arm64" ;;
  x86_64) RID="osx-x64" ;;
  *) echo "Error: Unsupported CPU architecture: $(uname -m)" >&2; exit 1 ;;
esac

for required_file in "$PROJECT_FILE" "$ICON_FILE"; do
  if [[ ! -f "$required_file" ]]; then
    echo "Error: Required file not found: $required_file" >&2
    exit 1
  fi
done

PUBLISH_DIR="$OUTPUT_DIR/publish-$RID"
APP_DIR="$OUTPUT_DIR/$APP_NAME.app"
RESOURCES_DIR="$APP_DIR/Contents/Resources"

mkdir -p "$OUTPUT_DIR"
echo "Publishing $APP_NAME for $RID..."
dotnet publish "$PROJECT_FILE" --configuration Release --runtime "$RID" --self-contained true --output "$PUBLISH_DIR" -p:UseAppHost=true

if [[ ! -f "$PUBLISH_DIR/$APP_NAME" ]]; then
  echo "Error: Executable not found: $PUBLISH_DIR/$APP_NAME" >&2
  echo "Check the project's AssemblyName and OutputType properties." >&2
  exit 1
fi

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$RESOURCES_DIR"
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/$APP_NAME"

# Convert the PNG into an Apple icon file (.icns).
ICONSET_DIR="$(mktemp -d "${TMPDIR:-/tmp}/novavms-icon.XXXXXX")/AppIcon.iconset"
mkdir -p "$ICONSET_DIR"
trap 'rm -rf "$(dirname "$ICONSET_DIR")"' EXIT
for size in 16 32 128 256 512; do
  sips -s format png -z "$size" "$size" "$ICON_FILE" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null
  double_size=$((size * 2))
  sips -s format png -z "$double_size" "$double_size" "$ICON_FILE" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/AppIcon.icns"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>$APP_NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleName</key><string>$APP_NAME</string>
  <key>CFBundleDisplayName</key><string>$APP_NAME</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# Ad-hoc signing is useful for local testing. Public distribution requires
# a Developer ID signature and Apple notarization.
codesign --force --deep --sign - "$APP_DIR"

printf '\nCreated: %s\nLaunch: open "%s"\n' "$APP_DIR" "$APP_DIR"

