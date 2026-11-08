#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Build the signed iPhone package on macOS with Xcode." >&2
  exit 1
fi
: "${CODESIGN_KEY:?Set CODESIGN_KEY to the Apple signing identity in Keychain}"
: "${CODESIGN_PROVISION:?Set CODESIGN_PROVISION to the installed provisioning profile name or UUID}"
: "${APPLICATION_ID:?Set APPLICATION_ID to the bundle identifier matching that profile}"
BUILD_NUMBER="${BUILD_NUMBER:-1}"
OUTPUT="${OUTPUT_DIRECTORY:-$ROOT/build/out/mobile/ios}"
mkdir -p "$OUTPUT"
xcodebuild -version
dotnet publish "$ROOT/src/NovaVMS.Mobile.iOS/NovaVMS.Mobile.iOS.csproj" \
  -c Release -f net10.0-ios -r ios-arm64 \
  -p:ArchiveOnBuild=true -p:BuildIpa=true \
  "-p:ApplicationId=$APPLICATION_ID" "-p:ApplicationVersion=$BUILD_NUMBER" \
  "-p:CodesignKey=$CODESIGN_KEY" "-p:CodesignProvision=$CODESIGN_PROVISION" \
  "-p:IpaPackageDir=$OUTPUT/"
IPA="$(find "$OUTPUT" -maxdepth 1 -name '*.ipa' -print -quit)"
if [[ -z "$IPA" ]]; then
  echo "Build did not create an IPA in $OUTPUT." >&2
  exit 1
fi
shasum -a 256 "$IPA"
echo "Created: $IPA"
