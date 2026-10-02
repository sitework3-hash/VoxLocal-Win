#!/usr/bin/env bash
# Creates a distributable DMG from an already built VoxLocal.app.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

[[ "$(uname -s)" == "Darwin" ]] || die "package_macos.sh must run on macOS"
[[ -d "$APP_BUNDLE" ]] || die "app bundle missing; run ./scripts/build_app.sh first"

VERSION="${VOXLOCAL_VERSION:-1.1.0-dev}"
ARCH="$(uname -m)"
DMG="$DIST_DIR/VoxLocal-macOS-$ARCH-$VERSION.dmg"
STAGING="$DIST_DIR/dmg-root"

log "Verifying app bundle before packaging..."
codesign --verify --deep --strict "$APP_BUNDLE" || die "app signature verification failed"

rm -rf "$STAGING" "$DMG"
mkdir -p "$STAGING"
ditto "$APP_BUNDLE" "$STAGING/VoxLocal.app"
ln -s /Applications "$STAGING/Applications"
cat > "$STAGING/README.txt" <<'EOF'
VoxLocal for macOS

1. Drag VoxLocal.app into Applications.
2. On the first launch, right-click VoxLocal and choose Open.
3. Allow Microphone, Speech Recognition and Accessibility when macOS asks.
4. Hold Option + Space to dictate; press Esc to cancel.

This development build uses an ad-hoc signature. A future public release can
use Apple Developer ID signing and notarization without changing application
data or settings.
EOF

log "Creating $(basename "$DMG")..."
hdiutil create \
  -volname "VoxLocal" \
  -srcfolder "$STAGING" \
  -ov \
  -format UDZO \
  "$DMG" >/dev/null
rm -rf "$STAGING"

hdiutil verify "$DMG" >/dev/null || die "DMG verification failed"
shasum -a 256 "$DMG" > "$DMG.sha256"
log "Package ready: $DMG"
log "Checksum: $DMG.sha256"
