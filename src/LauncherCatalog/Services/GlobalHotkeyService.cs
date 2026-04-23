using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace LauncherCatalog.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly int _hotkeyId = 0x4C43;
    private IntPtr _windowHandle;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public bool Register(IntPtr windowHandle, HotkeyGesture gesture)
    {
        Unregister();

        var virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (windowHandle == IntPtr.Zero || virtualKey == 0)
        {
            return false;
        }

        var modifiers = ToNativeModifiers(gesture.Modifiers) | ModNoRepeat;
        if (!RegisterHotKey(windowHandle, _hotkeyId, modifiers, (uint)virtualKey))
        {
            return false;
        }

        _windowHandle = windowHandle;
        _isRegistered = true;
        return true;
    }

    public void Unregister()
    {
        if (!_isRegistered || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(_windowHandle, _hotkeyId);
        _windowHandle = IntPtr.Zero;
        _isRegistered = false;
    }

    public IntPtr HandleWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint nativeModifiers = 0;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            nativeModifiers |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            nativeModifiers |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            nativeModifiers |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            nativeModifiers |= ModWin;
        }

        return nativeModifiers;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
