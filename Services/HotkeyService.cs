using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Lingo.Services
{
    public class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_ID_TEXT = 9000;
        public const int HOTKEY_ID_SNIP = 9001;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;

        private IntPtr _windowHandle;
        private HwndSource? _source;
        public event EventHandler? HotkeyPressed;
        public event EventHandler? SnipHotkeyPressed;

        public void Register(IntPtr windowHandle, uint modifiers = MOD_CONTROL | MOD_ALT, uint key = 0x54) // 0x54 = 'T'
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            // Register Text Translation hotkey (default Ctrl + Alt + T)
            UnregisterHotKey(_windowHandle, HOTKEY_ID_TEXT);
            RegisterHotKey(_windowHandle, HOTKEY_ID_TEXT, modifiers, key);

            // Register Screen Snip hotkey (Ctrl + Alt + S, 0x53 = 'S')
            UnregisterHotKey(_windowHandle, HOTKEY_ID_SNIP);
            RegisterHotKey(_windowHandle, HOTKEY_ID_SNIP, MOD_CONTROL | MOD_ALT, 0x53);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_TEXT)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                }
                else if (id == HOTKEY_ID_SNIP)
                {
                    SnipHotkeyPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID_TEXT);
                UnregisterHotKey(_windowHandle, HOTKEY_ID_SNIP);
                _source?.RemoveHook(HwndHook);
                _windowHandle = IntPtr.Zero;
            }
        }
    }
}
