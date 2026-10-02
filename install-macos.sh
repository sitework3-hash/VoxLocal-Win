#!/usr/bin/env bash
# Build and install VoxLocal for macOS without Homebrew or administrator access.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

MODEL="small"
INSTALL_DIR="$HOME/Applications"
SKIP_MODEL=0
NO_LAUNCH=0

usage() {
  cat <<'EOF'
Usage: ./install-macos.sh [options]

Options:
  --model NAME       Whisper model to install (default: small)
  --install-dir DIR  Application destination (default: ~/Applications)
  --skip-model       Build and install without downloading a model
  --no-launch        Do not launch VoxLocal after installation
  -h, --help         Show this help

Recommended models:
  base              Fastest, about 148 MB
  small             Balanced default, about 488 MB
  large-v3-turbo    Best quality on a powerful Apple Silicon Mac, about 1.6 GB
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --model)
      [[ $# -ge 2 ]] || { echo "[voxlocal:error] --model requires a value" >&2; exit 2; }
      MODEL="$2"
      shift 2
      ;;
    --install-dir)
      [[ $# -ge 2 ]] || { echo "[voxlocal:error] --install-dir requires a value" >&2; exit 2; }
      INSTALL_DIR="$2"
      shift 2
      ;;
    --skip-model) SKIP_MODEL=1; shift ;;
    --no-launch) NO_LAUNCH=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "[voxlocal:error] Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "[voxlocal:error] This installer must be run on macOS." >&2
  exit 1
fi

case "$MODEL" in
  tiny|tiny.en|base|base.en|small|small.en|medium|large-v3|large-v3-turbo) ;;
  *) echo "[voxlocal:error] Unsupported Whisper model: $MODEL" >&2; exit 2 ;;
esac

if ! xcode-select -p >/dev/null 2>&1; then
  echo "[voxlocal] Xcode Command Line Tools are required."
  echo "[voxlocal] Starting Apple's installer; rerun this script after it finishes."
  xcode-select --install || true
  exit 1
fi

if [[ "$(uname -m)" == "arm64" ]]; then
  echo "[voxlocal] Apple Silicon detected; whisper.cpp will use Metal acceleration."
else
  echo "[voxlocal] Intel Mac detected; Metal support depends on the installed GPU and macOS."
fi

printf '\n[voxlocal] Preparing project-local build tools...\n'
./scripts/bootstrap.sh

printf '\n[voxlocal] Running macOS tests...\n'
./scripts/test.sh

if [[ $SKIP_MODEL -eq 0 ]]; then
  printf '\n[voxlocal] Installing Whisper model: %s\n' "$MODEL"
  ./scripts/download_model.sh "$MODEL" -y
else
  echo "[voxlocal] Model download skipped. Install one later in VoxLocal settings."
fi

printf '\n[voxlocal] Building VoxLocal.app...\n'
./scripts/build_app.sh

mkdir -p "$INSTALL_DIR"
DESTINATION="$INSTALL_DIR/VoxLocal.app"

# Stop only the copy at the destination being updated. A development build
# launched from elsewhere is intentionally left alone.
if pgrep -x VoxLocal >/dev/null 2>&1 && [[ -d "$DESTINATION" ]]; then
  RUNNING_PATH="$(osascript -e 'tell application "System Events" to get POSIX path of application file of first process whose name is "VoxLocal"' 2>/dev/null || true)"
  if [[ "$RUNNING_PATH" == "$DESTINATION" || "$RUNNING_PATH" == "$DESTINATION/" ]]; then
    osascript -e 'tell application "VoxLocal" to quit' 2>/dev/null || true
    for _ in {1..20}; do
      pgrep -x VoxLocal >/dev/null 2>&1 || break
      sleep 0.25
    done
  fi
fi

printf '\n[voxlocal] Installing into %s...\n' "$DESTINATION"
STAGING="$INSTALL_DIR/.VoxLocal.installing.app"
rm -rf "$STAGING"
ditto "$SCRIPT_DIR/dist/VoxLocal.app" "$STAGING"
rm -rf "$DESTINATION"
mv "$STAGING" "$DESTINATION"
codesign --verify --deep --strict "$DESTINATION"

printf '\n[voxlocal] Installation complete: %s\n' "$DESTINATION"
echo '[voxlocal] On first use, allow Microphone and Accessibility access when macOS asks.'
echo '[voxlocal] Hold Option + Space to dictate; press Esc to cancel.'

if [[ $NO_LAUNCH -eq 0 ]]; then
  open "$DESTINATION"
  echo '[voxlocal] VoxLocal launched. Its microphone icon is in the menu bar.'
fi
