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
    private static readonly int[] ClipboardRetryDelaysMs = [20, 40, 80, 160, 300];
    private readonly Dispatcher _dispatcher;

    public TextInserter(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public static nint CaptureForegroundWindow() => GetForegroundWindow();

    public static bool IsWindowValid(nint window) => window != 0 && IsWindow(window);

    public static string DescribeCapturedWindow(nint window)
    {
        if (!IsWindowValid(window))
            return "invalid window";

        try
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            var process = Process.GetProcessById((int)processId);
            return $"process={process.ProcessName}, pid={process.Id}, title=\"{GetWindowTitle(window)}\"";
        }
        catch
        {
            return "window details unavailable";
        }
    }

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
                await SetClipboardTextWithRetryAsync(text, cancellationToken);
                return InsertionOutcome.SecureField;
            }

            if (mode == InsertionMode.ClipboardOnly || targetWindow == 0)
            {
                await SetClipboardTextWithRetryAsync(text, cancellationToken);
                return InsertionOutcome.ClipboardOnly;
            }

            if (!IsWindowValid(targetWindow))
            {
                AppLog.Shared.Info("Paste skipped: captured target window is no longer valid");
                await SetClipboardTextWithRetryAsync(text, cancellationToken);
                return InsertionOutcome.ClipboardOnly;
            }

            var snapshot = await SnapshotClipboardWithRetryAsync(cancellationToken);
            await SetClipboardTextWithRetryAsync(text, cancellationToken);
            var clipboardSequence = GetClipboardSequenceNumber();

            if (GetForegroundWindow() != targetWindow)
            {
                ActivateTargetWindow(targetWindow);
                await Task.Delay(220, cancellationToken);
            }

            if (GetForegroundWindow() != targetWindow)
            {
                AppLog.Shared.Info($"Paste skipped: {DescribeWindow(targetWindow)} did not become foreground");
                _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
                return InsertionOutcome.ClipboardOnly;
            }

            if (!await WaitForModifierKeysReleasedAsync(cancellationToken))
            {
                AppLog.Shared.Info("Paste skipped: keyboard modifiers remained pressed");
                _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
                return InsertionOutcome.ClipboardOnly;
            }

            var useUnicodeTyping = UsesUnicodeTyping(targetWindow);
            var useDirectPasteMessage = IsProcess(targetWindow, "notepad");
            var sent = useUnicodeTyping
                ? TypeUnicodeText(text)
                : useDirectPasteMessage
                    ? SendPasteMessage(targetWindow)
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
                               (useUnicodeTyping ? "Unicode typing" :
                                useDirectPasteMessage ? "WM_PASTE" : "Ctrl+V") +
                               "; text will be kept in clipboard for 5 seconds");
            _ = RestoreClipboardAfterDelayAsync(snapshot, clipboardSequence);
            return InsertionOutcome.Pasted;
        }).Task.Unwrap();
    }

    private async Task RestoreClipboardAfterDelayAsync(ClipboardSnapshot snapshot, uint clipboardSequence)
    {
        await Task.Delay(ClipboardRestoreDelay);
        await _dispatcher.InvokeAsync(async () =>
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

            if (await RestoreClipboardWithRetryAsync(snapshot.Data))
                AppLog.Shared.Info("Previous clipboard restored after 5 seconds");
            else
                AppLog.Shared.Info("Previous clipboard restoration failed because the clipboard remained busy");
        }).Task.Unwrap();
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

    private static async Task<ClipboardSnapshot> SnapshotClipboardWithRetryAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= ClipboardRetryDelaysMs.Length; attempt++)
        {
            try
            {
                var snapshot = new Dictionary<string, object>();
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
            catch (COMException) when (attempt < ClipboardRetryDelaysMs.Length)
            {
                await Task.Delay(ClipboardRetryDelaysMs[attempt], cancellationToken);
            }
            catch
            {
                break;
            }
        }

        return new ClipboardSnapshot(new Dictionary<string, object>(), false);
    }

    private static async Task SetClipboardTextWithRetryAsync(
        string text,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException) when (attempt < ClipboardRetryDelaysMs.Length)
            {
                AppLog.Shared.Info($"Clipboard busy; retrying text write (attempt {attempt + 2}/{ClipboardRetryDelaysMs.Length + 1})");
                await Task.Delay(ClipboardRetryDelaysMs[attempt], cancellationToken);
            }
        }
    }

    private static async Task<bool> RestoreClipboardWithRetryAsync(Dictionary<string, object> snapshot)
    {
        for (var attempt = 0; attempt <= ClipboardRetryDelaysMs.Length; attempt++)
        {
            try
            {
                if (snapshot.Count == 0)
                {
                    Clipboard.Clear();
                }
                else
                {
                    var data = new DataObject();
                    foreach (var (format, value) in snapshot)
                        data.SetData(format, value);
                    Clipboard.SetDataObject(data, true);
                }
                return true;
            }
            catch (COMException) when (attempt < ClipboardRetryDelaysMs.Length)
            {
                await Task.Delay(ClipboardRetryDelaysMs[attempt]);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool UsesUnicodeTyping(nint window) =>
        IsProcess(window, "sublime_text") || IsProcess(window, "Termius");

    private static bool IsProcess(nint window, string processName)
    {
        try
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return Process.GetProcessById((int)processId).ProcessName.Equals(
                processName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
            return "";
        var builder = new System.Text.StringBuilder(length + 1);
        _ = GetWindowText(window, builder, builder.Capacity);
        return builder.ToString().Replace('"', '\'');
    }

    private static bool IsWindow(nint window) => IsWindowNative(window);

    private static string DescribeWindow(nint window) => DescribeCapturedWindow(window);

    private static void ActivateTargetWindow(nint window)
    {
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(window, out _);
        var attached = targetThread != 0 && targetThread != currentThread &&
                       AttachThreadInput(currentThread, targetThread, true);
        try
        {
            ShowWindow(window, 9);
            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetFocus(window);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, targetThread, false);
        }
    }


    private static async Task<bool> WaitForModifierKeysReleasedAsync(CancellationToken cancellationToken)
    {
        const int attempts = 6;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsModifierKeyDown(0x10) && !IsModifierKeyDown(0x11) &&
                !IsModifierKeyDown(0x12) && !IsModifierKeyDown(0x5B) &&
                !IsModifierKeyDown(0x5C))
                return true;
            await Task.Delay(30, cancellationToken);
        }
        return false;
    }

    private static bool IsModifierKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static bool SendPasteMessage(nint targetWindow)
    {
        var targetThread = GetWindowThreadProcessId(targetWindow, out _);
        if (targetThread == 0)
            return false;

        var currentThread = GetCurrentThreadId();
        var attached = targetThread != currentThread &&
                       AttachThreadInput(currentThread, targetThread, true);
        try
        {
            var info = new GuiThreadInfo { CbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(targetThread, ref info) || info.HwndFocus == 0)
            {
                AppLog.Shared.Info("WM_PASTE skipped: focused control was not found");
                return false;
            }

            return PostMessage(info.HwndFocus, 0x0302, 0, 0);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, targetThread, false);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint CbSize;
        public uint Flags;
        public nint HwndActive;
        public nint HwndFocus;
        public nint HwndCapture;
        public nint HwndMenuOwner;
        public nint HwndMoveSize;
        public nint HwndCaret;
        public System.Drawing.Rectangle RcCaret;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);



    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

}
