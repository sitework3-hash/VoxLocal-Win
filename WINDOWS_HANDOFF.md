# VoxLocal for Windows — handoff notes

This document is the quick starting point for the next maintainer or Codex
session. It describes the Windows port that lives on branch `windows-port`.

## Current project state

- Working repository: `C:\Users\MZuser\Desktop\CodexProjects\VoxLocal`
- User fork: `https://github.com/sitework3-hash/VoxLocal-Win.git`
- Working branch: `windows-port`
- Upstream `origin` remains the original macOS repository.
- The Windows desktop application is implemented in `windows/`; it does not
  modify the original macOS implementation.
- The build output is `dist\windows\VoxLocal\VoxLocal.exe`.

## Everyday commands

Run from the repository root in PowerShell:

```powershell
.\scripts\windows\test.ps1
.\scripts\windows\build.ps1
.\scripts\windows\run.ps1
```

`build.ps1` publishes the application to `dist\windows\VoxLocal`. Stop a
running `VoxLocal.exe` before rebuilding to avoid locked files. The project
uses only local dependencies under `vendor\`; do not install Homebrew,
CocoaPods, or a global package manager for this project.

First-time setup scripts:

```powershell
.\scripts\windows\bootstrap.ps1
.\scripts\windows\download_model.ps1 -Model base
.\scripts\windows\download_sherpa_model.ps1
```

## Runtime data on this PC

All user/runtime data is outside the repository:

| Item | Location |
| --- | --- |
| Settings | `%LOCALAPPDATA%\VoxLocal\settings.json` |
| Transcription history (last 5) | `%LOCALAPPDATA%\VoxLocal\history.json` |
| Application log | `%LOCALAPPDATA%\VoxLocal\logs\voxlocal.log` |
| Whisper and Sherpa models | `%LOCALAPPDATA%\VoxLocal\models\` |
| Local build dependencies | `vendor\` (repository, ignored by Git) |

Temporary WAV recordings are deleted after each completed or cancelled
dictation. Do not commit user settings, logs, models, `vendor`, `bin`, `obj`,
or `dist`.

## Architecture map

| Area | Main files | Responsibility |
| --- | --- | --- |
| Program startup | `windows/VoxLocal.Windows/Program.cs` | Creates services, tray icon, overlay and controller. |
| Dictation flow | `App/DictationController.cs` | State machine: hotkey → audio → recognition → optional refinement → history → insertion. |
| Hotkey/audio | `Services/GlobalHotkeyService.cs`, `Services/AudioRecorder.cs` | Default hold-to-talk `Alt + Space`; `Esc` cancels. |
| Recognition | `Services/SherpaTOneTranscriber.cs`, `Services/WhisperTranscriber.cs` | Local Sherpa-ONNX Russian streaming model by default; local whisper.cpp alternative. |
| Text insertion | `Services/TextInserter.cs` | Clipboard handling, target activation, synthetic input and editor-specific fallback. |
| Persistent history | `Core/TranscriptionHistoryStore.cs` | Keeps exactly five newest successful transcripts in `history.json`. |
| Settings UI | `UI/SettingsWindow.cs` | Tabs: `Основное` and `История`. |
| Tray/overlay | `UI/TrayController.cs`, `UI/OverlayWindow.cs` | Tray menu, settings entry point and recording status. |
| Tests | `windows/VoxLocal.Tests/Program.cs` | Console test suite, currently 10 checks. |

## Expected user behaviour

- Hold `Alt + Space`, speak, then release to transcribe.
- `Esc` cancels the current recording.
- The Sherpa model is the default fast local Russian engine. Whisper `base` is
  available from Settings when multilingual accuracy is more important.
- Settings open with a double-click on the VoxLocal tray icon near the clock.
- The `История` tab presents the latest five finished transcripts with a copy
  button. It starts collecting entries from the version that introduced it;
  historical logs intentionally do not contain transcript text.

## Clipboard and editor compatibility

For normal applications VoxLocal places the transcript in the clipboard and
sends `Ctrl+V` to the window that had focus when recording began.

- The prior clipboard is restored after **5 seconds**.
- Restoration uses the Windows clipboard sequence number. If the user copies
  anything during those five seconds, VoxLocal leaves that newer clipboard
  untouched.
- Password fields are never filled automatically.
- Sublime Text and Termius use direct Unicode typing instead of clipboard
  paste. This avoids Sublime ignoring synthetic paste and avoids Termius
  misreading a restored image as a terminal image paste.
- If a target application runs elevated while VoxLocal does not, Windows may
  block synthetic input. Use the same privilege level or copy manually.

When diagnosing a failed insert, first inspect the latest log entries:

```powershell
Get-Content "$env:LOCALAPPDATA\VoxLocal\logs\voxlocal.log" -Tail 80
```

Useful messages include the captured target process and whether the operation
used `Ctrl+V` or `Unicode typing`. The log deliberately does not write the
recognized text.

## Permissions and optional features

- Enable microphone access for desktop apps in Windows Settings → Privacy &
  security → Microphone.
- Windows has no macOS Accessibility permission. The microphone permission is
  the only required privacy permission for VoxLocal.
- Ollama is optional and must never be installed automatically. If already
  installed, the user can run `ollama pull qwen2.5:3b` and enable Refinement
  with a local loopback endpoint only.

## Change and release checklist

1. Check `git status` before edits; preserve unrelated user changes.
2. Use `apply_patch` for source edits.
3. Stop only the VoxLocal process before rebuilding.
4. Run `scripts\windows\test.ps1`, then build and run the app.
5. Confirm `dist\windows\VoxLocal\VoxLocal.exe` exists and a `VoxLocal`
   process is running.
6. Commit focused changes on `windows-port` and push to `userfork`.

The original macOS README is upstream material. See `README_WINDOWS.md` for
user-facing Windows setup; this file is the maintainer/debugging handoff.
