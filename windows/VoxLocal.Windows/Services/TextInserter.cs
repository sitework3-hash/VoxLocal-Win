using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public enum InsertionOutcome
{
    Pasted,
    ClipboardOnly,
    SecureField
}

public sealed class TextInserter
{
    private static readonly TimeSpan ClipboardRestoreDelay = TimeSpan.FromSeconds(5);
    private readonly Dispatcher _dispatcher;

    public TextInserter(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public static nint CaptureForegroundWindow() => GetForegroundWindow();

    public async Task<InsertionOutcome> InsertAsync(
        string text,
        nint targetWindow,
        InsertionMode mode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return InsertionOutcome.ClipboardOnly;

        return await _dispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsFocusedPasswordField())
            {
                Clipboard.SetText(text);
                return InsertionOutcome.SecureField;
            }

            if (mode == InsertionMode.ClipboardOnly || targetWindow == 0)
            {
                Clipboard.SetText(text);
                return InsertionOutcome.ClipboardOnly;
            }

            var snapshot = SnapshotClipboard();
            Clipboard.SetText(text);
            var clipboardSequence = GetClipboardSequenceNumber();

            if (GetForegroundWindow() != targetWindow)
            {
                ShowWindow(targetWindow, 9);
                BringWindowToTop(targetWindow);
                SetForegroundWindow(targetWindow);
                await Task.Delay(180, cancellationToken);
            }

            if (GetForegroundWindow() != targetWindow)
            {
                AppLog.Shared.Info($"Paste skipped: {DescribeWindow(targetWindow)} did not become foreground");
                _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
                return InsertionOutcome.ClipboardOnly;
            }

            var sent = IsSublimeTextWindow(targetWindow)
                ? TypeUnicodeText(text)
                : PostCtrlV();
            if (!sent)
            {
                _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
                return InsertionOutcome.ClipboardOnly;
            }

            await Task.Delay(450, CancellationToken.None);
            // Keep the recognized phrase in the clipboard. SendInput reports
            // whether Windows accepted the keystrokes, not whether a custom
            // editor actually changed its text. This gives users a reliable
            // Ctrl+V fallback without losing their dictation.
            AppLog.Shared.Info($"Paste sent to {DescribeWindow(targetWindow)} using " +
                               (IsSublimeTextWindow(targetWindow) ? "Unicode typing" : "Ctrl+V") +
                               "; text will be kept in clipboard for 5 seconds");
            _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
            return InsertionOutcome.Pasted;
        }).Task.Unwrap();
    }

    private async Task RestoreClipboardAfterDelayAsync(ClipboardSnapshot snapshot, uint clipboardSequence)
    {
        await Task.Delay(ClipboardRestoreDelay);
        await _dispatcher.InvokeAsync(() =>
        {
            // Do not overwrite anything copied by the user after dictation.
            if (GetClipboardSequenceNumber() != clipboardSequence)
            {
                AppLog.Shared.Info("Clipboard changed by user; previous clipboard was not restored");
                return;
            }

            if (!snapshot.Captured)
            {
                AppLog.Shared.Info("Previous clipboard could not be read; restoration skipped");
                return;
            }

            RestoreClipboard(snapshot.Data);
            AppLog.Shared.Info("Previous clipboard restored after 5 seconds");
        });
    }

    private static bool IsFocusedPasswordField()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element is not null &&
                   element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) is true;
        }
        catch
        {
            return false;
        }
    }

    private static ClipboardSnapshot SnapshotClipboard()
    {
        var snapshot = new Dictionary<string, object>();
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is not null)
            {
                foreach (var format in data.GetFormats(false))
                {
                    try
                    {
                        var value = data.GetData(format, false);
                        if (value is not null)
                            snapshot[format] = value;
                    }
                    catch { }
                }
            }
            return new ClipboardSnapshot(snapshot, true);
        }
        catch
        {
            return new ClipboardSnapshot(snapshot, false);
        }
    }

    private static void RestoreClipboard(Dictionary<string, object> snapshot)
    {
        try
        {
            if (snapshot.Count == 0)
            {
                Clipboard.Clear();
                return;
            }

            var data = new DataObject();
            foreach (var (format, value) in snapshot)
                data.SetData(format, value);
            Clipboard.SetDataObject(data, true);
        }
        catch { }
    }

    private static bool IsSublimeTextWindow(nint window)
    {
        try
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return Process.GetProcessById((int)processId).ProcessName.Equals(
                "sublime_text", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string DescribeWindow(nint window)
    {
        try
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch { return "unknown window"; }
    }

    private static bool PostCtrlV()
    {
        var inputs = new[]
        {
            KeyboardInput(0x11, false),
            KeyboardInput(0x56, false),
            KeyboardInput(0x56, true),
            KeyboardInput(0x11, true)
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static bool TypeUnicodeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(UnicodeInput(character, false));
            inputs.Add(UnicodeInput(character, true));
        }

        return inputs.Count > 0 &&
               SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == inputs.Count;
    }

    private static Input KeyboardInput(ushort key, bool keyUp) => new()
    {
        Type = 1,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = key,
                Flags = keyUp ? 0x0002u : 0
            }
        }
    };

    private static Input UnicodeInput(char character, bool keyUp) => new()
    {
        Type = 1,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                ScanCode = character,
                Flags = 0x0004u | (keyUp ? 0x0002u : 0)
            }
        }
    };

    private sealed record ClipboardSnapshot(Dictionary<string, object> Data, bool Captured);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // INPUT uses the size of the largest member of the native union
    // (MOUSEINPUT, 32 bytes on x64), even when we submit KEYBDINPUT values.
    // Without the explicit size SendInput rejects the structure as too small.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

}
