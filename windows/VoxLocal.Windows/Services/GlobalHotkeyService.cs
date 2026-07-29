using System.Runtime.InteropServices;
using System.Windows.Threading;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkSpace = 0x20;
    private const int VkEscape = 0x1B;
    private const int VkControl = 0x11;
    private const uint LlkhfAltDown = 0x20;

    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly HookProcedure _procedure;
    private nint _hook;
    private bool _mainDown;

    public event Action? MainKeyDown;
    public event Action? MainKeyUp;
    public event Action? EscapePressed;
    public bool CaptureEscape { get; set; }

    public GlobalHotkeyService(Dispatcher dispatcher, SettingsStore settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _procedure = HookCallback;
    }

    public void Start()
    {
        if (_hook != 0)
            return;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _procedure, moduleHandle, 0);
        if (_hook == 0)
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), "Не удалось установить глобальную горячую клавишу.");
        AppLog.Shared.Info("Keyboard hook installed");
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return CallNextHookEx(_hook, code, wParam, lParam);

        var message = unchecked((int)wParam);
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var down = message is WmKeyDown or WmSysKeyDown;
        var up = message is WmKeyUp or WmSysKeyUp;

        if (data.VirtualKeyCode == VkEscape && down && CaptureEscape)
        {
            _dispatcher.BeginInvoke(EscapePressed);
            return 1;
        }

        if (data.VirtualKeyCode == VkSpace)
        {
            var alt = (data.Flags & LlkhfAltDown) != 0 || IsKeyDown(0x12);
            var modifiersMatch = _settings.Current.UseExactAltSpace
                ? alt
                : alt && IsKeyDown(VkControl);

            if (down && modifiersMatch)
            {
                if (!_mainDown)
                {
                    _mainDown = true;
                    _dispatcher.BeginInvoke(MainKeyDown);
                }
                return 1;
            }
            if (up && _mainDown)
            {
                _mainDown = false;
                _dispatcher.BeginInvoke(MainKeyUp);
                return 1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
