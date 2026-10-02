# VoxLocal — agent handoff

This file is the first document an AI coding agent should read before changing
VoxLocal. The repository maintains two applications: a stable Windows port and
a macOS application under active development.

## Non-negotiable platform boundary

- **Windows code is stable and must remain working.** Do not modify `windows/`,
  `scripts/windows/`, `install.ps1`, or `uninstall.ps1` while implementing a
  macOS task unless the task explicitly concerns Windows.
- Windows baseline: tag `windows-v1.1-stable`, commit `4a00b56`.
- macOS work: branch `feature/macos` until the user approves merging it.
- Current repository: `https://github.com/sitework3-hash/VoxLocal-Win.git`.
- Upstream macOS repository is `https://github.com/romarayt/VoxLocal.git`.
- Do not commit `vendor/`, `dist/`, `bin/`, `obj/`, models, logs, API keys or
  user history. They are ignored or explicitly private.

## What the product does

VoxLocal is a menu-bar voice dictation app. It records while the global shortcut
is held, performs local Whisper transcription, optionally refines the transcript,
and inserts it into the application that was active before recording.

Shared product guarantees:

- raw audio is temporary and deleted after success, cancellation and failure;
- logs never contain audio, transcript text, clipboard contents or API keys;
- cloud refinement is opt-in and always falls back to the raw transcript;
- secure/password fields are not automatically filled;
- users can use clipboard-only insertion without Accessibility permission.

## macOS architecture

- `Sources/VoxLocal/VoxLocalMain.swift`: executable entry point.
- `Sources/VoxLocalCore/App/AppDelegate.swift`: dependency graph, menu-bar app,
  windows and global hotkey registration.
- `Sources/VoxLocalCore/App/DictationController.swift`: session state machine
  and complete pipeline: record → preview → Whisper → refinement → history →
  insertion.
- `Sources/VoxLocalCore/Audio/AudioRecorder.swift`: AVAudioEngine capture,
  16 kHz WAV writing and mic level.
- `Sources/VoxLocalCore/Transcription/WhisperTranscriber.swift`: bundled
  whisper.cpp subprocess and JSON parser.
- `Sources/VoxLocalCore/Transcription/LiveSpeechPreview.swift`: optional
  on-device Apple Speech partial preview; Whisper remains authoritative.
- `Sources/VoxLocalCore/Transcription/ModelManager.swift`: model discovery,
  validation and downloads.
- `Sources/VoxLocalCore/Refinement/`: Ollama provider, OpenAI-compatible
  provider, prompts, safeguard and Keychain API-key storage.
- `Sources/VoxLocalCore/History/TranscriptionHistoryStore.swift`: five newest
  successful entries in local `history.json`.
- `Sources/VoxLocalCore/Insertion/`: Accessibility insertion, clipboard paste,
  clipboard restoration and secure-field detection.
- `Sources/VoxLocalCore/UI/`: SwiftUI settings, onboarding, overlay and menu bar.
- `Tests/VoxLocalTests/`: state, parser, privacy, insertion, refinement,
  settings and history tests.

## macOS commands

Run on macOS from the repository root:

```bash
./scripts/bootstrap.sh       # local CMake + whisper.cpp with Metal
./scripts/test.sh            # Swift tests
./scripts/build_app.sh       # dist/VoxLocal.app, ad-hoc signed
./scripts/package_macos.sh   # verified dist/VoxLocal-macOS-*.dmg
./scripts/run.sh
./install-macos.sh           # build, test, install to ~/Applications
```

The GitHub `Platform tests` workflow runs Windows and macOS independently.
The macOS release workflow runs for tags matching `macos-v*`.

## macOS runtime data

| Data | Location |
| --- | --- |
| Models | `~/Library/Application Support/VoxLocal/models/` |
| History | `~/Library/Application Support/VoxLocal/history.json` |
| Settings | macOS `UserDefaults` for `org.voxlocal.VoxLocal` |
| Logs | `~/Library/Logs/VoxLocal/` |
| API key | macOS Keychain service `org.voxlocal.VoxLocal.cloud-refinement`, account `api-key` |
| Temporary audio | system temporary directory, deleted after each session |

Never inspect or print history, clipboard data, audio or the Keychain secret in
diagnostics.

## Current macOS configuration

- macOS 14+, Apple Silicon recommended.
- Option + Space, press-and-hold by default; toggle mode is supported.
- Whisper `small` is the default for fresh Mac installations. Existing users
  keep whichever model name is already persisted in their settings.
- Optional cloud refinement defaults to Polza.ai:
  `https://polza.ai/api/v1`, model `google/gemini-2.5-flash`.
- Alternative profile: `deepseek/deepseek-chat`.
- Local Ollama remains available at loopback only.
- Cloud refinement is disabled by default and requires a Keychain-stored key.

## Safe implementation rules

1. Read `MACOS_HANDOFF.md` and the relevant source/tests before editing.
2. Check `git status`; preserve unrelated user changes.
3. Make focused commits, push `feature/macos`, and wait for both CI jobs.
4. If CI fails, use GitHub check annotations; do not claim success without a
   green macOS job.
5. Add tests for new parsing, networking, settings, privacy and fallback logic.
6. Keep network providers injectable/testable and refuse redirects.
7. Do not run a real Polza.ai request without the user's private API key.
8. Do not merge to `main` or alter the Windows stable tag without explicit
   approval.

## Manual MacBook acceptance checklist

- [ ] macOS 14+ and Apple Silicon tested.
- [ ] Microphone permission and denied-permission fallback.
- [ ] Speech Recognition permission; preview failure must not stop recording.
- [ ] Accessibility insertion and clipboard-only fallback.
- [ ] Option + Space hold, toggle mode and Esc cancellation.
- [ ] Whisper base/small/large-v3-turbo and long recording ending.
- [ ] Warp/Terminal, browser, Telegram, editor and secure/password field.
- [ ] Clipboard restored only when no other app changed it.
- [ ] History stores five entries and copy/clear work.
- [ ] Keychain save/delete; no key in settings/logs/history.
- [ ] Polza timeout/error/bad response uses raw transcript.
- [ ] Login launch and sleep/wake behavior.
- [ ] DMG install and first-launch Gatekeeper instructions.

## How a new agent should continue

```bash
git clone --branch feature/macos https://github.com/sitework3-hash/VoxLocal-Win.git
cd VoxLocal-Win
# read AGENTS.md and MACOS_HANDOFF.md
./scripts/test.sh
```

The next priorities are manual MacBook validation, performance benchmarks for
Whisper models on Apple Silicon, and Developer ID signing/notarization when
Apple Developer credentials are available.
