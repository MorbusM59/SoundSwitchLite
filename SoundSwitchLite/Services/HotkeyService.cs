using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundSwitchLite.Services;

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private int _nextId = 0x9000;

    public void Initialize(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a global hotkey. Returns the hotkey ID, or -1 on failure.
    /// </summary>
    public int RegisterHotkey(int modifiers, int key, Action callback)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, (uint)modifiers, (uint)key))
            return -1;
        _hotkeys[id] = callback;
        return id;
    }

    /// <summary>
    /// Unregisters a previously registered hotkey by its ID.
    /// </summary>
    public void UnregisterHotkey(int id)
    {
        if (_hotkeys.ContainsKey(id))
        {
            UnregisterHotKey(_hwnd, id);
            _hotkeys.Remove(id);
        }
    }

    /// <summary>
    /// Unregisters all registered hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var id in _hotkeys.Keys.ToList())
            UnregisterHotKey(_hwnd, id);
        _hotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeys.TryGetValue(id, out var action))
            {
                action();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
