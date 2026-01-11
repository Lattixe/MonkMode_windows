using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using static MonkMode.Services.NativeMethods;

namespace MonkMode.Services;

/// <summary>
/// Tracks the currently active foreground window and provides its bounds.
/// Fires an event whenever the active window changes or moves.
/// </summary>
public class WindowTrackerService : IDisposable
{
    private readonly DispatcherTimer _trackingTimer;
    private readonly IntPtr _overlayHandle;
    private IntPtr _lastTrackedWindow = IntPtr.Zero;
    private RECT _lastWindowRect;
    private bool _isDisposed;

    /// <summary>
    /// Event fired when the active window bounds change.
    /// </summary>
    public event EventHandler<WindowBoundsEventArgs>? WindowBoundsChanged;

    /// <summary>
    /// Event fired when there's no valid window to track (e.g., desktop focused).
    /// </summary>
    public event EventHandler? NoValidWindow;

    /// <summary>
    /// The currently tracked window handle.
    /// </summary>
    public IntPtr CurrentWindowHandle => _lastTrackedWindow;

    public WindowTrackerService(IntPtr overlayWindowHandle)
    {
        _overlayHandle = overlayWindowHandle;
        _trackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps for smooth tracking
        };
        _trackingTimer.Tick += OnTrackingTick;
    }

    /// <summary>
    /// Start tracking the active window.
    /// </summary>
    public void StartTracking()
    {
        _trackingTimer.Start();
    }

    /// <summary>
    /// Stop tracking the active window.
    /// </summary>
    public void StopTracking()
    {
        _trackingTimer.Stop();
    }

    private void OnTrackingTick(object? sender, EventArgs e)
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();

            // Skip if it's our own overlay window
            if (foregroundWindow == _overlayHandle || foregroundWindow == IntPtr.Zero)
            {
                return;
            }

            // Check if this is a valid application window (not desktop/shell)
            if (!IsValidApplicationWindow(foregroundWindow))
            {
                NoValidWindow?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Get window bounds
            if (GetWindowRect(foregroundWindow, out RECT windowRect))
            {
                // Check if anything changed
                bool windowChanged = foregroundWindow != _lastTrackedWindow;
                bool boundsChanged = !RectsEqual(windowRect, _lastWindowRect);

                if (windowChanged || boundsChanged)
                {
                    _lastTrackedWindow = foregroundWindow;
                    _lastWindowRect = windowRect;

                    // Get window info for debugging/logging
                    string windowClass = GetWindowClassName(foregroundWindow);
                    string processName = GetProcessNameFromWindow(foregroundWindow);

                    WindowBoundsChanged?.Invoke(this, new WindowBoundsEventArgs
                    {
                        WindowHandle = foregroundWindow,
                        Bounds = new Rect(
                            windowRect.Left,
                            windowRect.Top,
                            windowRect.Width,
                            windowRect.Height
                        ),
                        WindowClassName = windowClass,
                        ProcessName = processName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window tracking error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the window is a valid application window that should be tracked.
    /// Filters out desktop, shell, and other system windows.
    /// </summary>
    private bool IsValidApplicationWindow(IntPtr hWnd)
    {
        // Check if visible
        if (!IsWindowVisible(hWnd))
            return false;

        // Check if minimized
        if (IsIconic(hWnd))
            return false;

        // Filter out shell/desktop windows
        IntPtr shellWindow = GetShellWindow();
        IntPtr desktopWindow = GetDesktopWindow();

        if (hWnd == shellWindow || hWnd == desktopWindow)
            return false;

        // Filter by class name - skip system/shell classes
        string className = GetWindowClassName(hWnd);
        string[] invalidClasses = 
        {
            "Progman",           // Desktop
            "WorkerW",           // Desktop worker
            "Shell_TrayWnd",     // Taskbar
            "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow",
            "Windows.UI.Core.CoreWindow" // Some system windows
        };

        if (invalidClasses.Contains(className, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        char[] buffer = new char[256];
        int length = GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string GetProcessNameFromWindow(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static bool RectsEqual(RECT a, RECT b)
    {
        return a.Left == b.Left && a.Top == b.Top && 
               a.Right == b.Right && a.Bottom == b.Bottom;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _trackingTimer.Stop();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event arguments containing the bounds of the tracked window.
/// </summary>
public class WindowBoundsEventArgs : EventArgs
{
    public required IntPtr WindowHandle { get; init; }
    public required Rect Bounds { get; init; }
    public required string WindowClassName { get; init; }
    public required string ProcessName { get; init; }
}
