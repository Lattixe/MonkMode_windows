using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MonkMode.Services;
using static MonkMode.Services.NativeMethods;
using Application = System.Windows.Application;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MonkMode.Views;

/// <summary>
/// The overlay window that creates the "spotlight" effect over the active window.
/// Uses CombinedGeometry to cut a transparent hole in a black overlay.
/// </summary>
public partial class OverlayWindow : Window
{
    private WindowTrackerService? _windowTracker;
    private readonly DispatcherTimer _sessionTimer;
    private DateTime _sessionStartTime;
    private int _interventionCount;
    private IntPtr _windowHandle;

    // Session configuration
    public string TaskName { get; set; } = "Focus Session";
    public int IntensityLevel { get; set; } = 2; // 1=Flow, 2=Deep, 3=Blackout
    public List<string> BlockedProcesses { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();

    // Events
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public OverlayWindow()
    {
        InitializeComponent();

        // Session timer for elapsed time display
        _sessionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionTimer.Tick += OnSessionTimerTick;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get the window handle for P/Invoke operations
        _windowHandle = new WindowInteropHelper(this).Handle;

        // Set up the screen geometry to cover entire screen
        SetupScreenGeometry();

        // Initialize the window tracker
        _windowTracker = new WindowTrackerService(_windowHandle);
        _windowTracker.WindowBoundsChanged += OnWindowBoundsChanged;
        _windowTracker.NoValidWindow += OnNoValidWindow;

        // Apply intensity settings
        ApplyIntensityLevel();

        // Update UI
        TaskNameText.Text = $"Task: {TaskName}";
        UpdateIntensityText();

        // Register global hotkey (Ctrl+Shift+Q to exit)
        RegisterExitHotkey();

        // Start tracking and timer
        _sessionStartTime = DateTime.Now;
        _windowTracker.StartTracking();
        _sessionTimer.Start();

        // Subscribe to system blocker events if available
        var systemBlocker = Application.Current.Properties["SystemBlocker"] as SystemBlockerService;
        if (systemBlocker != null)
        {
            systemBlocker.ProcessBlocked += OnProcessBlocked;
        }
    }

    private void SetupScreenGeometry()
    {
        // Get primary screen dimensions
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        // Set the full screen geometry
        ScreenGeometry.Rect = new Rect(0, 0, screenWidth, screenHeight);

        // Initialize spotlight to center of screen (will be updated by tracker)
        SpotlightGeometry.Rect = new Rect(
            screenWidth / 4, 
            screenHeight / 4, 
            screenWidth / 2, 
            screenHeight / 2);
    }

    private void OnWindowBoundsChanged(object? sender, WindowBoundsEventArgs e)
    {
        // Update the spotlight cutout to match the active window
        Dispatcher.Invoke(() =>
        {
            // Convert screen coordinates to WPF coordinates (handle DPI scaling)
            var dpiScale = VisualTreeHelper.GetDpi(this);
            var bounds = new Rect(
                e.Bounds.X / dpiScale.DpiScaleX,
                e.Bounds.Y / dpiScale.DpiScaleY,
                e.Bounds.Width / dpiScale.DpiScaleX,
                e.Bounds.Height / dpiScale.DpiScaleY);

            // Apply slight padding for visual appeal
            const double padding = 2;
            var paddedBounds = new Rect(
                bounds.X - padding,
                bounds.Y - padding,
                bounds.Width + (padding * 2),
                bounds.Height + (padding * 2));

            // Update spotlight geometry
            SpotlightGeometry.Rect = paddedBounds;

            // Update border position if visible
            if (SpotlightBorder.Visibility == Visibility.Visible)
            {
                Canvas.SetLeft(SpotlightBorder, paddedBounds.X);
                Canvas.SetTop(SpotlightBorder, paddedBounds.Y);
                SpotlightBorder.Width = paddedBounds.Width;
                SpotlightBorder.Height = paddedBounds.Height;
            }
        });
    }

    private void OnNoValidWindow(object? sender, EventArgs e)
    {
        // When desktop or invalid window is focused, show full blackout
        Dispatcher.Invoke(() =>
        {
            // Make spotlight very small or invisible
            SpotlightGeometry.Rect = new Rect(0, 0, 0, 0);
        });
    }

    private void ApplyIntensityLevel()
    {
        double opacity = IntensityLevel switch
        {
            1 => 0.5,   // Flow - Dimmed
            2 => 0.9,   // Deep Work - Dark
            3 => 1.0,   // Blackout - Pitch Black
            _ => 0.9
        };

        DarkOverlay.Opacity = opacity;

        // Show spotlight border only at lower intensity levels
        SpotlightBorder.Visibility = IntensityLevel < 3 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void UpdateIntensityText()
    {
        string intensityName = IntensityLevel switch
        {
            1 => "Flow (50%)",
            2 => "Deep Work (90%)",
            3 => "Blackout (100%)",
            _ => "Unknown"
        };
        IntensityText.Text = $"Intensity: {intensityName}";
    }

    private void OnSessionTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _sessionStartTime;
        SessionTimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
    }

    private void OnProcessBlocked(object? sender, ProcessBlockedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _interventionCount++;
            InterventionBadge.Visibility = Visibility.Visible;
            InterventionCountText.Text = $"{_interventionCount} blocked";

            // Flash the badge briefly
            InterventionBadge.Opacity = 1;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                InterventionBadge.Opacity = 0.8;
                timer.Stop();
            };
            timer.Start();
        });
    }

    #region Hotkey Registration

    private const int HOTKEY_ID = 9000;

    private void RegisterExitHotkey()
    {
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WndProc);

        // Register Ctrl+Shift+Q
        RegisterHotKey(_windowHandle, HOTKEY_ID, 
            MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_Q);
    }

    private void UnregisterExitHotkey()
    {
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            EndSession();
            handled = true;
        }
        return IntPtr.Zero;
    }

    #endregion

    #region Session Management

    private void EndSession()
    {
        var sessionDuration = DateTime.Now - _sessionStartTime;

        // Fire session ended event
        SessionEnded?.Invoke(this, new SessionEndedEventArgs
        {
            TaskName = TaskName,
            Duration = sessionDuration,
            InterventionCount = _interventionCount,
            IntensityLevel = IntensityLevel
        });

        Close();
    }

    #endregion

    #region UI Event Handlers

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        EndSession();
    }

    private void ExitButton_MouseEnter(object sender, MouseEventArgs e)
    {
        ExitButton.Opacity = 1.0;
    }

    private void ExitButton_MouseLeave(object sender, MouseEventArgs e)
    {
        ExitButton.Opacity = 0.7;
    }

    #endregion

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Cleanup
        _sessionTimer.Stop();
        _windowTracker?.StopTracking();
        _windowTracker?.Dispose();
        UnregisterExitHotkey();

        // Unsubscribe from system blocker
        var systemBlocker = Application.Current.Properties["SystemBlocker"] as SystemBlockerService;
        if (systemBlocker != null)
        {
            systemBlocker.ProcessBlocked -= OnProcessBlocked;
        }
    }
}

public class SessionEndedEventArgs : EventArgs
{
    public required string TaskName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int InterventionCount { get; init; }
    public required int IntensityLevel { get; init; }
}
