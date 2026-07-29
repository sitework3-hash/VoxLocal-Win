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

            if (GetForegroundWindow() != targetWindow)
            {
                ShowWindow(targetWindow, 5);
                SetForegroundWindow(targetWindow);
                await Task.Delay(120, cancellationToken);
            }

            if (!PostCtrlV())
                return InsertionOutcome.ClipboardOnly;

            await Task.Delay(450, CancellationToken.None);
            RestoreClipboard(snapshot);
            return InsertionOutcome.Pasted;
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

    private static Dictionary<string, object> SnapshotClipboard()
    {
        var snapshot = new Dictionary<string, object>();
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null)
                return snapshot;
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
        catch { }
        return snapshot;
    }

    private static void RestoreClipboard(Dictionary<string, object> snapshot)
    {
        if (snapshot.Count == 0)
            return;
        try
        {
            var data = new DataObject();
            foreach (var (format, value) in snapshot)
                data.SetData(format, value);
            Clipboard.SetDataObject(data, true);
        }
        catch { }
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
