# VoxLocal macOS — maintainer handoff

Read [AGENTS.md](AGENTS.md) first. This document records the exact macOS state
and the practical continuation procedure for a coding agent running on a
MacBook.

## Repository state

- Remote: `https://github.com/sitework3-hash/VoxLocal-Win.git`
- Development branch: `feature/macos`
- Stable Windows branch/tag: `windows-port` / `windows-v1.1-stable`
- First macOS preview tag: `macos-v0.1.0-preview`
- macOS plan: [MACOS_DEVELOPMENT_PLAN.md](MACOS_DEVELOPMENT_PLAN.md)
- User instructions: [README_MACOS.md](README_MACOS.md)

The Windows process on the development PC is unrelated to the macOS branch and
must not be stopped or rebuilt for macOS-only changes.

## Implemented macOS functionality

- Native Swift/AppKit/SwiftUI menu-bar app.
- Global Option + Space shortcut with hold and toggle modes.
- Esc cancellation and explicit state machine.
- AVAudioEngine recording to temporary 16 kHz PCM WAV.
- whisper.cpp v1.9.1 built with Metal and bundled into the app.
- Downloadable Whisper models from tiny through large-v3-turbo.
- On-device Apple Speech live preview; failure is non-fatal.
- Accessibility insertion, synthetic Command+V fallback and clipboard restore.
- Secure-field protection.
- Five-entry local transcription history with copy and clear.
- Optional local Ollama refinement.
- Optional Polza.ai/OpenAI-compatible refinement.
- Gemini 2.5 Flash, DeepSeek Chat and custom model profiles.
- API-key storage in macOS Keychain.
- Strict refinement safeguard and raw-transcript fallback.
- Russian and English UI.
- Installer for `~/Applications`.
- CI-built, ad-hoc-signed DMG and tag-based preview release workflow.

## First setup on a MacBook

```bash
git clone --branch feature/macos https://github.com/sitework3-hash/VoxLocal-Win.git
cd VoxLocal-Win
xcode-select --install  # only if Command Line Tools are missing
./install-macos.sh --model small
```

For a high-memory Apple Silicon Mac, also test:

```bash
./scripts/download_model.sh large-v3-turbo
```

Grant Microphone, Speech Recognition and Accessibility in System Settings →
Privacy & Security. Permission prompts and global input behavior cannot be
meaningfully validated in headless GitHub Actions.

## CI and release diagnostics

Public workflow status:

```text
https://github.com/sitework3-hash/VoxLocal-Win/actions
```

REST endpoints useful to an agent without GitHub CLI:

```text
https://api.github.com/repos/sitework3-hash/VoxLocal-Win/actions/runs?branch=feature%2Fmacos&per_page=5
https://api.github.com/repos/sitework3-hash/VoxLocal-Win/actions/runs/<RUN_ID>/jobs
https://api.github.com/repos/sitework3-hash/VoxLocal-Win/check-runs/<JOB_ID>/annotations
https://api.github.com/repos/sitework3-hash/VoxLocal-Win/releases
```

The platform workflow intentionally emits compiler failures as public check
annotations. Never add output that can contain an API key or transcript.

Create a preview release only after the platform workflow is green:

```bash
git tag -a macos-v0.2.0-preview -m "VoxLocal macOS preview"
git push userfork macos-v0.2.0-preview
```

The release workflow builds from source, runs tests, creates a DMG and publishes
its SHA-256 file. It is a prerelease until manual validation is complete.

## Privacy-sensitive design notes

### Live preview

`LiveSpeechPreview` sets `requiresOnDeviceRecognition = true`. It never supplies
the final transcript. `AudioRecorder` forwards native audio buffers only while
the preview request exists. If Speech authorization is declined or no local
recognizer is available, `DictationController` continues normally with Whisper.
Partial text is neither logged nor stored in history.

### Cloud refinement

Only final recognized text is sent, never audio. `OpenAICompatibleRefinementProvider`:

- validates the explicit HTTP(S) base URL;
- appends `/chat/completions` once;
- refuses redirects;
- uses an ephemeral URL session;
- classifies provider HTTP errors;
- returns through `RefinementPipeline`, which applies `RefinementSafeguard`;
- falls back to the raw transcript for all failures.

The API key is stored only through `KeychainSecretStore`. Do not move it into
`UserDefaults`, environment files, logs or repository secrets unless implementing
signed CI with explicit user approval.

### History

History necessarily contains transcript text, so it is kept outside logs in
`~/Library/Application Support/VoxLocal/history.json`. Capacity is exactly five.
Treat this file as private user content.

## Known limitations before stable release

1. The DMG is ad-hoc signed, not notarized. First launch requires right-click →
   Open. Stable distribution needs an Apple Developer account, Developer ID
   Application certificate and notarization credentials.
2. Runtime microphone, Speech and Accessibility behavior still needs testing on
   physical Apple Silicon hardware.
3. Apple Speech live preview may not support every locale offline; this must
   degrade silently and must never block Whisper.
4. Performance benchmarks for base/small/large-v3-turbo are not yet recorded.
5. Onboarding text was originally Ollama-oriented and should be reviewed against
   the new cloud-provider settings during physical-device UX testing.

## Recommended immediate next work

1. Review onboarding so Polza is optional and never required.
2. Add a setting to disable live preview explicitly if desired.
3. Run the complete manual checklist from `AGENTS.md` on a MacBook.
4. Record timings and choose model/thread defaults by memory class.
5. After manual approval, merge `feature/macos` into `main` without deleting the
   Windows tree or tags.
6. Rename the GitHub repository from `VoxLocal-Win` to `VoxLocal` only with user
   approval; GitHub redirects old URLs automatically.

## Completion definition

The macOS implementation is code-complete only when CI is green. It is
release-complete only after physical MacBook testing and, for a warning-free
public release, Developer ID signing and notarization.
