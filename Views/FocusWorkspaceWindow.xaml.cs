using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MonkMode.Services;
using static MonkMode.Services.NativeMethods;
using Color = System.Windows.Media.Color;

namespace MonkMode.Views;

/// <summary>
/// Full-screen focus workspace. 
/// Provides isolated environment with only selected windows visible.
/// </summary>
public partial class FocusWorkspaceWindow : Window
{
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _windowWatcher;
    private readonly SystemBlockerService _blockerService;
    private DateTime _sessionEndTime;
    private DateTime _sessionStartTime;
    private int _totalSeconds;
    private IntPtr _windowHandle;
    private List<WindowInfo> _allowedWindows = new();
    private IntPtr _intruderWindow = IntPtr.Zero; // Track windows opened outside workspace
    
    // Blocking settings
    public bool EnableDnsBlocking { get; set; } = true;
    public bool EnableProcessBlocking { get; set; } = false; // Off by default - can be aggressive
    public bool EnableFocusAssist { get; set; } = true;

    // P/Invoke for window management
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;

    public string TaskName { get; set; } = "Focus";
    public int DurationMinutes { get; set; } = 25;
    public List<WindowInfo> SelectedWindows { get; set; } = new();

    public event EventHandler<FocusSessionResult>? SessionEnded;

    /// <summary>
    /// Force end the session from external caller (like global hotkey)
    /// </summary>
    public void ForceEnd()
    {
        Debug.WriteLine("[Workspace] ForceEnd called from external source");
        Dispatcher.Invoke(() => TryEndSession());
    }

    public FocusWorkspaceWindow()
    {
        InitializeComponent();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1) // Update once per second - reduces CPU/stutter
        };
        _countdownTimer.Tick += OnCountdownTick;

        _windowWatcher = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500) // Check frequently for intruders
        };
        _windowWatcher.Tick += OnWindowWatcherTick;
        
        // Initialize blocker service
        _blockerService = new SystemBlockerService();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Activated += OnActivated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Ensure window handle is ready
            var helper = new WindowInteropHelper(this);
            _windowHandle = helper.Handle;
            
            // If handle is zero, wait a moment for it to initialize
            if (_windowHandle == IntPtr.Zero)
            {
                Debug.WriteLine("[Workspace] Window handle not ready, waiting...");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _windowHandle = helper.Handle;
                    if (_windowHandle != IntPtr.Zero)
                    {
                        ContinueInitialization();
                    }
                    else
                    {
                        Debug.WriteLine("[Workspace] Window handle still not ready after delay");
                        ContinueInitialization(); // Continue anyway
                    }
                };
                timer.Start();
                return; // Exit early, ContinueInitialization will be called by timer
            }
            
            ContinueInitialization();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Critical error in OnLoaded: {ex.Message}");
            System.Windows.MessageBox.Show($"Error starting workspace: {ex.Message}");
            Close();
        }
    }

    private void ContinueInitialization()
    {
        try
        {
            _allowedWindows = SelectedWindows?.ToList() ?? new List<WindowInfo>();

            Debug.WriteLine($"[Workspace] Loaded with {_allowedWindows.Count} allowed windows");

            // Setup UI
            TaskNameText.Text = TaskName;
            
            // Initialize timer
            _totalSeconds = DurationMinutes * 60;
            _sessionStartTime = DateTime.Now;
            _sessionEndTime = _sessionStartTime.AddMinutes(DurationMinutes);
            UpdateTimerDisplay();

            // Start timers
            _countdownTimer.Start();
            _windowWatcher.Start();

            // Start pulse animation
            try
            {
                var pulse = (Storyboard)FindResource("PulseAnimation");
                pulse?.Begin(this);
            }
            catch { }

            // Hide taskbar
            try
            {
                HideTaskbar();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Workspace] Error hiding taskbar: {ex.Message}");
                // Continue anyway - taskbar hiding is not critical
            }

            // Start distraction blocking
            StartDistactionBlocking();

            // Register hotkey for ending session
            try
            {
                RegisterHotkey();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Workspace] Error registering hotkey: {ex.Message}");
                // Continue anyway - hotkey is optional
            }

            // Send workspace to back and bring windows to front after a short delay
            _startupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _startupTimer.Tick += OnStartupTimerTick;
            _startupTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in ContinueInitialization: {ex.Message}");
            System.Windows.MessageBox.Show($"Error starting workspace: {ex.Message}");
            Close();
        }
    }

    private DispatcherTimer? _startupTimer;

    private void OnStartupTimerTick(object? sender, EventArgs e)
    {
        _startupTimer?.Stop();
        _startupTimer = null;

        try
        {
            // Minimize other windows first
            MinimizeOtherWindows();
            
            // Send workspace to bottom of z-order
            SetWindowPos(_windowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            
            // Forcefully show and bring each allowed window to front
            foreach (var window in _allowedWindows)
            {
                if (window.Handle != IntPtr.Zero && IsWindow(window.Handle))
                {
                    // Restore if minimized
                    if (IsIconic(window.Handle))
                    {
                        ShowWindow(window.Handle, SW_RESTORE);
                    }
                    
                    // Show if hidden
                    if (!IsWindowVisible(window.Handle))
                    {
                        ShowWindow(window.Handle, SW_SHOW);
                    }
                    
                    // Bring to top
                    SetWindowPos(window.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    BringWindowToTop(window.Handle);
                    
                    Debug.WriteLine($"[Workspace] Showed window on startup: {window.Title}");
                }
            }
            
            // Set focus to first allowed window if available
            if (_allowedWindows.Count > 0 && _allowedWindows[0].Handle != IntPtr.Zero)
            {
                SetForegroundWindow(_allowedWindows[0].Handle);
            }
            
            Debug.WriteLine($"[Workspace] Startup complete - showed {_allowedWindows.Count} windows");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in startup timer: {ex.Message}");
        }
    }


    private void OnActivated(object? sender, EventArgs e)
    {
        // When workspace is activated, minimize any intruder windows
        MinimizeIntruder();
    }
    
    private void MinimizeIntruder()
    {
        if (_intruderWindow != IntPtr.Zero && IsWindow(_intruderWindow) && IsWindowVisible(_intruderWindow))
        {
            Debug.WriteLine($"[Workspace] Minimizing intruder: {_intruderWindow}");
            ShowWindow(_intruderWindow, 6); // SW_MINIMIZE
            _intruderWindow = IntPtr.Zero;
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        UpdateTimerDisplay();

        if (DateTime.Now >= _sessionEndTime)
        {
            EndSession(completed: true);
        }
    }

    private void OnWindowWatcherTick(object? sender, EventArgs e)
    {
        try
        {
            // Get current foreground window
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;
            
            // Ignore taskbar and system windows
            if (IsSystemWindow(foreground)) return;
            
            // Check if it's our workspace or an allowed window
            bool isAllowed = foreground == _windowHandle || 
                             _allowedWindows.Any(w => w.Handle == foreground);
            
            if (isAllowed)
            {
                // Focus returned to workspace - minimize any intruder window
                MinimizeIntruder();
            }
            else
            {
                // A non-workspace window has focus - track it as intruder
                // But only if it's a real window (visible, not minimized)
                if (IsWindowVisible(foreground) && !IsIconic(foreground))
                {
                    if (_intruderWindow != foreground)
                    {
                        _intruderWindow = foreground;
                        Debug.WriteLine($"[Workspace] Tracking intruder: {foreground}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in window watcher: {ex.Message}");
        }
    }
    
    private bool IsSystemWindow(IntPtr hWnd)
    {
        // Check for taskbar and other system windows we should ignore
        try
        {
            char[] className = new char[256];
            int len = GetClassName(hWnd, className, 256);
            if (len > 0)
            {
                string name = new string(className, 0, len);
                // Ignore taskbar, start menu, action center, etc.
                if (name.Contains("Shell_TrayWnd") || 
                    name.Contains("Shell_SecondaryTrayWnd") ||
                    name.Contains("Windows.UI.Core") ||
                    name.Contains("TaskListThumbnailWnd") ||
                    name.Contains("NotifyIconOverflowWindow") ||
                    name.Contains("Progman") ||
                    name.Contains("WorkerW"))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private string _lastDisplayedTime = "";
    
    private void UpdateTimerDisplay()
    {
        var remaining = _sessionEndTime - DateTime.Now;

        if (remaining.TotalSeconds <= 0)
        {
            if (TimerText.Text != "00:00")
                TimerText.Text = "00:00";
            return;
        }

        // Only update text if it changed (reduces rendering)
        var newTime = remaining.ToString(@"mm\:ss");
        if (newTime != _lastDisplayedTime)
        {
            _lastDisplayedTime = newTime;
            TimerText.Text = newTime;
            
            // Progress tracking (visual bar removed for cleaner UI)

            // Color feedback - only check when needed
            if (remaining.TotalSeconds <= 60)
            {
                TimerText.Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow
            }
            else if (remaining.TotalSeconds <= 10)
            {
                TimerText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
        }
    }

    private bool _isRestoringWindows = false;
    
    private void BringAllowedWindowsToFront(bool force = false)
    {
        // Prevent recursive calls and unnecessary operations
        if (_isRestoringWindows) return;
        
        if (_allowedWindows.Count == 0)
        {
            return;
        }
        
        // Check if any window actually needs restoration (unless forced)
        if (!force)
        {
            bool anyHidden = false;
            foreach (var window in _allowedWindows)
            {
                if (window.Handle != IntPtr.Zero && 
                    (IsIconic(window.Handle) || !IsWindowVisible(window.Handle)))
                {
                    anyHidden = true;
                    break;
                }
            }
            
            if (!anyHidden)
            {
                // All windows are already visible - no need to do anything
                return;
            }
        }
        
        _isRestoringWindows = true;
        
        try
        {
            Debug.WriteLine($"[Workspace] Restoring {_allowedWindows.Count} windows");
            
            // Send workspace to back
            SetWindowPos(_windowHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            
            foreach (var window in _allowedWindows)
            {
                if (window.Handle == IntPtr.Zero)
                    continue;
                
                try
                {
                    // Only restore if actually minimized
                    if (IsIconic(window.Handle))
                    {
                        ShowWindow(window.Handle, SW_RESTORE);
                    }
                    
                    // Only show if actually hidden
                    if (!IsWindowVisible(window.Handle))
                    {
                        ShowWindow(window.Handle, SW_SHOW);
                    }
                    
                    // Bring to top (but don't set foreground - avoids flicker)
                    BringWindowToTop(window.Handle);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Workspace] Error with window {window.Title}: {ex.Message}");
                }
            }

            HintPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in BringAllowedWindowsToFront: {ex.Message}");
        }
        finally
        {
            _isRestoringWindows = false;
        }
    }

    private void MinimizeOtherWindows()
    {
        var allowedHandles = _allowedWindows.Select(w => w.Handle).ToHashSet();
        allowedHandles.Add(_windowHandle); // Don't minimize ourselves

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    !allowedHandles.Contains(process.MainWindowHandle) &&
                    IsWindowVisible(process.MainWindowHandle) &&
                    !IsIconic(process.MainWindowHandle))
                {
                    // Minimize other windows
                    ShowWindow(process.MainWindowHandle, 6); // SW_MINIMIZE
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void EnforceWindowPolicy()
    {
        // This could be more aggressive - for now we just ensure allowed windows stay visible
        foreach (var window in _allowedWindows)
        {
            if (window.Handle != IntPtr.Zero && IsIconic(window.Handle))
            {
                // Restore if minimized
                ShowWindow(window.Handle, 9); // SW_RESTORE
            }
        }
    }

    private void HideTaskbar()
    {
        try
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            Debug.WriteLine($"[Workspace] Found taskbar handle: {taskbar}");
            if (taskbar != IntPtr.Zero)
            {
                ShowWindow(taskbar, SW_HIDE);
                Debug.WriteLine("[Workspace] Taskbar hidden");
            }

            IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(secondaryTaskbar, SW_HIDE);
                Debug.WriteLine("[Workspace] Secondary taskbar hidden");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in HideTaskbar: {ex.Message}");
        }
    }

    private void ShowTaskbar()
    {
        try
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                ShowWindow(taskbar, SW_SHOW);
                Debug.WriteLine("[Workspace] Taskbar restored");
            }

            IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(secondaryTaskbar, SW_SHOW);
                Debug.WriteLine("[Workspace] Secondary taskbar restored");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error in ShowTaskbar: {ex.Message}");
        }
    }

    #region Distraction Blocking

    private void StartDistactionBlocking()
    {
        try
        {
            // Configure DNS blocking
            if (EnableDnsBlocking)
            {
                _blockerService.SetBlockedDomains(SystemBlockerService.DefaultBlockedDomains);
                Debug.WriteLine($"[Workspace] DNS blocking: {SystemBlockerService.DefaultBlockedDomains.Length} domains");
            }

            // Configure process blocking (off by default)
            if (EnableProcessBlocking)
            {
                _blockerService.SetBlockedProcesses(SystemBlockerService.DefaultBlockedProcesses);
                Debug.WriteLine($"[Workspace] Process blocking: {SystemBlockerService.DefaultBlockedProcesses.Length} processes");
            }

            // Start all blocking (includes Focus Assist)
            _blockerService.StartBlocking();
            
            Debug.WriteLine("[Workspace] Distraction blocking started");
        }
        catch (UnauthorizedAccessException)
        {
            // DNS blocking requires admin - just log and continue
            Debug.WriteLine("[Workspace] DNS blocking requires admin privileges - skipping");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error starting distraction blocking: {ex.Message}");
        }
    }

    private void StopDistractionBlocking()
    {
        try
        {
            _blockerService.StopBlocking();
            Debug.WriteLine("[Workspace] Distraction blocking stopped");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error stopping distraction blocking: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Press Escape to try ending session (with commitment check)
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Debug.WriteLine("[Workspace] Escape pressed - showing commitment dialog");
            TryEndSession();
            e.Handled = true;
        }
    }

    private void WorkspaceArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Minimize any intruder windows first
        MinimizeIntruder();
        // Send workspace back to bottom so windows stay visible
        SendWorkspaceToBack();
    }
    
    /// <summary>
    /// Sends the workspace window to the back without touching other windows.
    /// This is a lightweight operation that prevents flicker.
    /// </summary>
    private void SendWorkspaceToBack()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            SetWindowPos(_windowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private void ControlBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        // Send workspace back to bottom so windows stay visible
        SendWorkspaceToBack();
    }

    private void ExtendButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[Workspace] +5 min button Click event!");
        DoExtend();
    }

    private void ExtendButton_PreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Debug.WriteLine("[Workspace] +5 min button PreviewClick event!");
        DoExtend();
        e.Handled = true;
    }

    private void DoExtend()
    {
        _sessionEndTime = _sessionEndTime.AddMinutes(5);
        _totalSeconds += 300;
        DurationMinutes += 5;
        UpdateTimerDisplay();
        TimerText.Foreground = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        // Send workspace to back so windows stay visible
        SendWorkspaceToBack();
    }

    private void WindowsButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[Workspace] Windows button Click event!");
        DoAddWindows();
    }

    private void WindowsButton_PreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Debug.WriteLine("[Workspace] Windows button PreviewClick event!");
        DoAddWindows();
        e.Handled = true;
    }

    private void DoAddWindows()
    {
        try
        {
            // Open window picker to add more windows
            var picker = new WindowPickerWindow();
            picker.TaskName = "Add Windows";
            picker.ExistingWorkspaceWindows = _allowedWindows.ToList(); // Pass current windows
            picker.LivePreviewMode = true; // Enable live previews
            
            var result = picker.ShowDialog();
            
            if (result == true && picker.SelectedWindowsList.Count > 0)
            {
                Debug.WriteLine($"[Workspace] Processing {picker.SelectedWindowsList.Count} selected windows");
                
                // Add newly selected windows to our allowed list (avoid duplicates)
                foreach (var newWindow in picker.SelectedWindowsList)
                {
                    if (!_allowedWindows.Any(w => w.Handle == newWindow.Handle))
                    {
                        _allowedWindows.Add(newWindow);
                        Debug.WriteLine($"[Workspace] Added new: {newWindow.Title}");
                    }
                }
            }
            
            // Send workspace to back and restore any hidden windows
            SendWorkspaceToBack();
            BringAllowedWindowsToFront(force: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error opening window picker: {ex.Message}");
        }
    }

    private void EndButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[Workspace] End button Click event!");
        TryEndSession();
    }

    private void EndButton_PreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Debug.WriteLine("[Workspace] End button PreviewClick event!");
        TryEndSession();
        e.Handled = true;
    }

    #endregion

    #region Hotkey

    private const int HOTKEY_END = 9002;

    private void RegisterHotkey()
    {
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WndProc);
        RegisterHotKey(_windowHandle, HOTKEY_END, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_Q);
    }

    private void UnregisterHotkey()
    {
        UnregisterHotKey(_windowHandle, HOTKEY_END);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_END)
        {
            TryEndSession();
            handled = true;
        }
        return IntPtr.Zero;
    }

    #endregion

    /// <summary>
    /// Attempt to end the session. If ending early, shows commitment dialog.
    /// </summary>
    private void TryEndSession()
    {
        // Check if session is complete (timer ran out)
        var remaining = _sessionEndTime - DateTime.Now;
        
        if (remaining.TotalSeconds <= 0)
        {
            // Timer complete - end normally
            EndSession(completed: true);
            return;
        }
        
        // Trying to end early - show commitment dialog
        Debug.WriteLine($"[Workspace] User trying to end early, {remaining.TotalMinutes:F1} min remaining");
        
        var dialog = new CommitmentDialog();
        dialog.SetTimeRemaining(remaining);
        dialog.Owner = this;
        
        var result = dialog.ShowDialog();
        
        if (result == true && dialog.UserGaveUp)
        {
            Debug.WriteLine("[Workspace] User confirmed giving up");
            EndSession(completed: false);
        }
        else
        {
            Debug.WriteLine("[Workspace] User returned to focus");
            // User chose to continue - bring windows back
            SendWorkspaceToBack();
        }
    }

    private void EndSession(bool completed)
    {
        Debug.WriteLine("[Workspace] EndSession called, completed=" + completed);
        
        try
        {
            _countdownTimer.Stop();
            _windowWatcher.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error stopping timers: {ex.Message}");
        }

        try
        {
            UnregisterHotkey();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error unregistering hotkey: {ex.Message}");
        }

        try
        {
            ShowTaskbar();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error showing taskbar: {ex.Message}");
        }

        try
        {
            StopDistractionBlocking();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error stopping blocking: {ex.Message}");
        }

        var actualDuration = DateTime.Now - _sessionStartTime;

        try
        {
            SessionEnded?.Invoke(this, new FocusSessionResult
            {
                TaskName = TaskName,
                PlannedDuration = TimeSpan.FromMinutes(DurationMinutes),
                ActualDuration = actualDuration,
                Completed = completed,
                StartTime = _sessionStartTime,
                EndTime = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Workspace] Error invoking SessionEnded: {ex.Message}");
        }

        Debug.WriteLine("[Workspace] Closing workspace window");
        
        // Force close
        try
        {
            Close();
        }
        catch
        {
            // If Close() fails, force it
            Environment.Exit(0);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _countdownTimer.Stop();
        _windowWatcher.Stop();
        ShowTaskbar();
        StopDistractionBlocking();
        _blockerService?.Dispose();
    }
}
