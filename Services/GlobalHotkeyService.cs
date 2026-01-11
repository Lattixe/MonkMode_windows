using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static MonkMode.Services.NativeMethods;

namespace MonkMode.Services;

/// <summary>
/// Manages system-wide global hotkeys.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private IntPtr _windowHandle;
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private int _nextHotkeyId = 1000;
    private bool _isDisposed;

    /// <summary>
    /// Initialize the hotkey service with a window handle.
    /// </summary>
    public void Initialize(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Register a global hotkey.
    /// </summary>
    /// <param name="modifiers">Key modifiers (MOD_CONTROL, MOD_SHIFT, etc.)</param>
    /// <param name="key">Virtual key code</param>
    /// <param name="action">Action to execute when hotkey is pressed</param>
    /// <returns>Hotkey ID if successful, -1 if failed</returns>
    public int RegisterHotkey(uint modifiers, uint key, Action action)
    {
        int id = _nextHotkeyId++;
        
        bool success = RegisterHotKey(_windowHandle, id, modifiers | MOD_NOREPEAT, key);
        
        if (success)
        {
            _hotkeyActions[id] = action;
            return id;
        }
        
        return -1;
    }

    /// <summary>
    /// Unregister a previously registered hotkey.
    /// </summary>
    public void UnregisterHotkey(int hotkeyId)
    {
        if (_hotkeyActions.ContainsKey(hotkeyId))
        {
            UnregisterHotKey(_windowHandle, hotkeyId);
            _hotkeyActions.Remove(hotkeyId);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            
            if (_hotkeyActions.TryGetValue(hotkeyId, out var action))
            {
                action.Invoke();
                handled = true;
            }
        }
        
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        // Unregister all hotkeys
        foreach (var id in _hotkeyActions.Keys.ToList())
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotkeyActions.Clear();
        
        _source?.RemoveHook(WndProc);
        _isDisposed = true;
        
        GC.SuppressFinalize(this);
    }
}
