# VoxLocal for Windows (MVP)

Windows implementation of VoxLocal keeps all speech recognition on the device:
audio is recorded locally, sent to the bundled `whisper-cli.exe`, and the
temporary WAV is removed after transcription. Optional Refinement talks only
to a loopback Ollama endpoint.

For future maintainers and debugging, see [WINDOWS_HANDOFF.md](WINDOWS_HANDOFF.md).

## Build and run

Run these commands in PowerShell from the repository root:

```powershell
.\scripts\windows\bootstrap.ps1
.\scripts\windows\test.ps1
.\scripts\windows\download_model.ps1 -Model base
.\scripts\windows\build.ps1
.\scripts\windows\run.ps1
```

`bootstrap.ps1` downloads only project-local dependencies into `vendor/`:
the .NET SDK and the official `whisper.cpp` Windows binary. No Homebrew,
CocoaPods, global package manager, or administrator access is used.

The finished application is at `dist\windows\VoxLocal\VoxLocal.exe`.

## First-run setup

Allow desktop applications to use the microphone in **Windows Settings →
Privacy & security → Microphone**. VoxLocal offers a button that opens this
page.

Windows has no macOS Accessibility permission. Automatic insertion uses the
clipboard and a synthetic `Ctrl+V`; secure password fields are intentionally
never filled. If the target app runs as administrator, Windows can block
synthetic input: in that case VoxLocal leaves the text in the clipboard for
manual `Ctrl+V`.

## Dictation

The default is **Alt + Space**, the closest Windows equivalent of macOS
Option + Space. Hold it to record, release it to transcribe and insert. While
recording, **Esc** cancels the session and deletes its temporary audio.

Windows normally reserves Alt + Space for the active window's system menu.
VoxLocal intercepts it while running. In Settings you can switch to the less
conflicting **Ctrl + Alt + Space** combination. If PowerToys or another
program owns the shortcut, choose that alternative.

The Windows build includes a local Russian **Sherpa-ONNX T-One** engine as its
low-latency default. In Settings, choose **Whisper — точнее** when you prefer
the more reliable multilingual `base` model, or **Sherpa-ONNX — быстрее
(русский)** for short Russian dictation. The Sherpa model is installed with:

```powershell
.\scripts\windows\download_sherpa_model.ps1
```

The project-local Sherpa runtime is bundled during the build; the model itself
is stored in `%LOCALAPPDATA%\VoxLocal\models`.

For recordings up to 12 seconds, VoxLocal automatically uses a reduced
Whisper audio context. This substantially lowers CPU latency for short
dictation without changing the selected model. Longer recordings use the full
context so their ending is preserved.

## Optional Ollama refinement

VoxLocal never installs Ollama. If it is already installed, run:

```powershell
ollama pull qwen2.5:3b
```

Then enable Refinement in VoxLocal Settings and enter `qwen2.5:3b`. The app
rejects non-local Ollama endpoints.
