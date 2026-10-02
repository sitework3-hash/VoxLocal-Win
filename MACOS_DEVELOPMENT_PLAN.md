# VoxLocal macOS parity plan

This plan adds macOS as a second maintained platform. It does not replace or
restructure the stable Windows application.

## Protection rules

- Stable Windows baseline: tag `windows-v1.1-stable` (`4a00b56`).
- macOS work happens on `feature/macos` until manually validated on a MacBook.
- `windows/`, `scripts/windows/`, `install.ps1`, and `uninstall.ps1` are outside
  the macOS implementation scope.
- CI must run Windows tests and macOS tests independently before macOS work is
  merged to `main`.
- Platform release artifacts and version numbers remain independent.

## Target configuration

- macOS 14+, optimized for Apple Silicon.
- Native Swift/AppKit/SwiftUI menu-bar application.
- Hold Option+Space by default; toggle mode remains available.
- whisper.cpp with Metal is the primary local engine.
- `small` is the new-install recommendation; `base` and `large-v3-turbo` remain
  selectable for speed and maximum accuracy respectively.
- Audio and raw transcription stay local unless cloud refinement is explicitly
  enabled by the user.

## Delivery stages

### Stage 1 — installability and build protection

- [x] Preserve and publish the current Windows stable tag.
- [x] Create the isolated `feature/macos` branch.
- [x] Add `install-macos.sh` and macOS user documentation.
- [x] Add macOS and Windows CI jobs.
- [ ] Validate installation on an Apple Silicon Mac.

### Stage 2 — functional parity

- [x] Persistent history of the five newest successful transcripts.
- [x] Live partial transcription in the recording overlay.
- [x] OpenAI-compatible/Polza.ai refinement provider.
- [x] Gemini Flash and DeepSeek Chat profiles.
- [x] API-key storage in macOS Keychain.
- [x] Provider connection test and safe raw-transcript fallback.
- [ ] Match Windows clipboard restoration and empty-transcript behavior.

### Stage 3 — performance and productization

- [ ] Benchmark `base`, `small`, and `large-v3-turbo` on Apple Silicon.
- [ ] Tune threads/context without reducing long-recording accuracy.
- [x] Add ad-hoc signed DMG release workflow.
- [ ] Add Developer ID signing and Apple notarization when credentials exist.
- [ ] Manual matrix: microphone, Accessibility, hotkey, editors, browsers,
      messengers, terminals, multiple displays, sleep/wake, and login launch.

## Acceptance criteria

- No modification to Windows runtime behavior without a dedicated Windows task.
- Both platform test suites pass.
- Temporary WAV files are deleted after success, cancellation, and failure.
- Logs never contain transcript text, audio, clipboard contents, or API keys.
- Cloud refinement is off by default and always falls back to local text.
- The app remains usable without cloud credentials or network access.
