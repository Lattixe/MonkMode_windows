using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Drawing;
using MonkMode.Services;
using MonkMode.Views;
using static MonkMode.Services.NativeMethods;
using Application = System.Windows.Application;

namespace MonkMode;

/// <summary>
/// Monk Mode - Focus Workspace Application
/// Ctrl+Shift+Space toggles focus mode on/off
/// </summary>
public partial class App : Application
{
    private LauncherWindow? _launcher;
    private FocusWorkspaceWindow? _workspace;
    private SystemBlockerService? _systemBlocker;
    private bool _isInFocusMode;
    
    // Global hotkey
    private const int HOTKEY_TOGGLE = 9999;
    private IntPtr _hotkeyWindowHandle;
    private HwndSource? _hwndSource;
    private Window? _hotkeyWindow;
    
    // System tray
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        _systemBlocker = new SystemBlockerService();
        
        // Create hidden window for global hotkey
        _hotkeyWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
            Title = "MonkModeHotkey"
        };
        _hotkeyWindow.SourceInitialized += (s, args) =>
        {
            _hotkeyWindowHandle = new WindowInteropHelper(_hotkeyWindow).Handle;
            _hwndSource = HwndSource.FromHwnd(_hotkeyWindowHandle);
            _hwndSource?.AddHook(WndProc);
            
            // Register Ctrl+Shift+Space as global hotkey
            bool registered = RegisterHotKey(_hotkeyWindowHandle, HOTKEY_TOGGLE, 
                MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x20); // 0x20 = VK_SPACE
            
            Debug.WriteLine($"[App] Global hotkey registered: {registered}");
        };
        _hotkeyWindow.Show();
        _hotkeyWindow.Hide();
        
        // Show launcher window
        _launcher = new LauncherWindow();
        _launcher.SessionRequested += OnLauncherSessionRequested;
        _launcher.Show();
        
        // Setup system tray icon
        SetupTrayIcon();
        
        // Exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_TOGGLE)
        {
            Debug.WriteLine($"[App] Hotkey pressed! IsInFocusMode={_isInFocusMode}");
            
            // Use dispatcher to ensure we're on the UI thread
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_isInFocusMode)
                {
                    // End focus session
                    EndFocusSession();
                }
                else
                {
                    // Toggle launcher (Spotlight-style)
                    ToggleLauncher();
                }
            }));
            
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Monk Mode (Ctrl+Shift+Space)",
            Visible = true
        };
        
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show (Ctrl+Shift+Space)", null, (_, _) => ShowLauncher());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Monk Mode", null, (_, _) => ExitApp());
        
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowLauncher();
        
        // Show notification that app is running
        _trayIcon.ShowBalloonTip(2000, "Monk Mode", 
            "Running in background. Press Ctrl+Shift+Space to focus.", 
            ToolTipIcon.Info);
    }

    private void ToggleLauncher()
    {
        if (_launcher == null)
        {
            // Create launcher if it doesn't exist
            _launcher = new LauncherWindow();
            _launcher.SessionRequested += OnLauncherSessionRequested;
            _launcher.Show();
            _launcher.BringToFront();
        }
        else if (_launcher.IsVisible && _launcher.WindowState != WindowState.Minimized)
        {
            // Launcher is visible - hide it (Spotlight-style toggle)
            _launcher.Hide();
        }
        else
        {
            // Launcher is hidden or minimized - show it
            _launcher.Show();
            _launcher.BringToFront();
        }
    }

    private void ShowLauncher()
    {
        if (_launcher == null)
        {
            _launcher = new LauncherWindow();
            _launcher.SessionRequested += OnLauncherSessionRequested;
        }
        
        _launcher.Show();
        _launcher.BringToFront();
    }
    
    private void ExitApp()
    {
        // Actually exit the application
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    private void EndFocusSession()
    {
        Debug.WriteLine("[App] EndFocusSession called");
        
        if (_workspace != null)
        {
            // Trigger the workspace to end
            _workspace.ForceEnd();
        }
        else
        {
            _isInFocusMode = false;
        }
    }

    private void OnLauncherSessionRequested(object? sender, FocusSessionRequest request)
    {
        if (_isInFocusMode) return;

        var picker = new WindowPickerWindow
        {
            TaskName = request.TaskName,
            DurationMinutes = request.DurationMinutes
        };
        picker.SessionRequested += OnWindowPickerCompleted;
        picker.Show();
        picker.Activate();
    }

    private void OnWindowPickerCompleted(object? sender, WorkspaceSessionRequest request)
    {
        StartFocusWorkspace(request);
    }

    private void StartFocusWorkspace(WorkspaceSessionRequest request)
    {
        if (_isInFocusMode) return;
        
        _isInFocusMode = true;
        
        Debug.WriteLine($"[App] StartFocusWorkspace: Task={request.TaskName}, Duration={request.DurationMinutes}min");
        Debug.WriteLine($"[App] Selected windows count: {request.SelectedWindows.Count}");
        foreach (var w in request.SelectedWindows)
        {
            Debug.WriteLine($"[App] - Window: {w.Title} (Handle: {w.Handle})");
        }
        
        _launcher?.Hide();
        
        _workspace = new FocusWorkspaceWindow
        {
            TaskName = request.TaskName,
            DurationMinutes = request.DurationMinutes,
            SelectedWindows = request.SelectedWindows
        };
        _workspace.SessionEnded += OnWorkspaceSessionEnded;
        _workspace.Show();
    }

    private void OnWorkspaceSessionEnded(object? sender, FocusSessionResult result)
    {
        _isInFocusMode = false;
        _workspace = null;
        
        // Show beautiful completion dialog
        var duration = result.ActualDuration;
        var durationText = duration.TotalMinutes >= 60 
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
            
        var completeDialog = new Views.SessionCompleteWindow
        {
            TaskName = result.TaskName,
            Duration = durationText,
            WasCompleted = result.Completed
        };
        completeDialog.ShowDialog();
        
        // Don't auto-show launcher - user can invoke it with Ctrl+Shift+Space if needed
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unregister hotkey
        if (_hotkeyWindowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_hotkeyWindowHandle, HOTKEY_TOGGLE);
        }
        _hwndSource?.RemoveHook(WndProc);
        
        // Dispose tray icon
        _trayIcon?.Dispose();
        _trayIcon = null;
        
        _systemBlocker?.EmergencyCleanup();
        _systemBlocker?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _systemBlocker?.EmergencyCleanup();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _systemBlocker?.EmergencyCleanup();
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _systemBlocker?.EmergencyCleanup();
        e.SetObserved();
    }
}
