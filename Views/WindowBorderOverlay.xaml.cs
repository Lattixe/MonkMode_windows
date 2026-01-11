using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static MonkMode.Services.NativeMethods;

namespace MonkMode.Views;

/// <summary>
/// A transparent overlay that draws a glowing border around a tracked window.
/// Used to visually highlight which windows are part of the focus workspace.
/// </summary>
public partial class WindowBorderOverlay : Window
{
    private readonly DispatcherTimer _positionTimer;
    private IntPtr _trackedWindowHandle;
    private bool _isAnimating;

    // Extended window styles for click-through
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // Fallback for 32-bit systems
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public WindowBorderOverlay()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps for smooth tracking
        };
        _positionTimer.Tick += OnPositionTick;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Make window click-through
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("[WindowBorderOverlay] Window handle not ready yet");
                return;
            }

            // Use 64-bit safe version
            IntPtr extendedStyle;
            if (IntPtr.Size == 8) // 64-bit
            {
                extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                if (extendedStyle == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    Debug.WriteLine($"[WindowBorderOverlay] Error getting window long: {Marshal.GetLastWin32Error()}");
                    return;
                }
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(extendedStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW));
            }
            else // 32-bit
            {
                int style = GetWindowLong32(hwnd, GWL_EXSTYLE);
                SetWindowLong32(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            }

            // Start fade-in animation
            StartFadeInAnimation();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowBorderOverlay] Error in OnLoaded: {ex.Message}");
            // Continue anyway - click-through is nice but not critical
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _positionTimer.Stop();
    }

    /// <summary>
    /// Start tracking a window and show the border overlay around it.
    /// </summary>
    public void TrackWindow(IntPtr windowHandle)
    {
        _trackedWindowHandle = windowHandle;
        
        // Initial position update
        UpdatePosition();
        
        // Start continuous tracking
        _positionTimer.Start();
    }

    /// <summary>
    /// Stop tracking and hide the overlay.
    /// </summary>
    public void StopTracking()
    {
        _positionTimer.Stop();
        _trackedWindowHandle = IntPtr.Zero;
    }

    private void OnPositionTick(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_trackedWindowHandle == IntPtr.Zero)
            return;

        // Check if window still exists and is visible
        if (!IsWindowVisible(_trackedWindowHandle))
        {
            // Window was closed or hidden - hide overlay
            if (IsVisible) Hide();
            return;
        }

        // Check if window is minimized
        if (IsIconic(_trackedWindowHandle))
        {
            if (IsVisible) Hide();
            return;
        }

        // Get window rect
        if (!GetWindowRect(_trackedWindowHandle, out RECT rect))
        {
            Debug.WriteLine("[WindowBorderOverlay] Failed to get window rect");
            return;
        }

        // Add padding for the glow effect
        const int padding = 10;
        
        // Update overlay position and size
        Left = rect.Left - padding;
        Top = rect.Top - padding;
        Width = rect.Width + (padding * 2);
        Height = rect.Height + (padding * 2);

        // Ensure visible
        if (!IsVisible)
        {
            Debug.WriteLine($"[WindowBorderOverlay] Showing overlay at {Left}, {Top}, size {Width}x{Height}");
            Show();
        }
    }

    private void StartFadeInAnimation()
    {
        if (_isAnimating) return;
        _isAnimating = true;

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1, // Full opacity for the window, border opacity controls visibility
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeIn.Completed += (s, e) => _isAnimating = false;
        BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Animate the border with a subtle pulse effect.
    /// </summary>
    public void StartPulseAnimation()
    {
        var pulseAnimation = new DoubleAnimation
        {
            From = 0.4,
            To = 0.7, // Visible pulse range
            Duration = TimeSpan.FromMilliseconds(1500),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        GlowBorder.Effect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, pulseAnimation);
    }
}
