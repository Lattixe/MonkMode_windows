using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MonkMode.Services;
using static MonkMode.Services.NativeMethods;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Brush = System.Windows.Media.Brush;

namespace MonkMode.Views;

/// <summary>
/// Floating pill timer shown during focus sessions.
/// Displays: Live indicator, task name, countdown timer, extend (+5), close.
/// </summary>
public partial class FocusPillWindow : Window
{
    private readonly DispatcherTimer _countdownTimer;
    private DateTime _sessionEndTime;
    private DateTime _sessionStartTime;
    private IntPtr _windowHandle;
    
    private const int HOTKEY_END_SESSION = 9001;

    public string TaskName { get; set; } = "Focus";
    public int DurationMinutes { get; set; } = 25;
    
    public event EventHandler<FocusSessionResult>? SessionEnded;

    public FocusPillWindow()
    {
        InitializeComponent();
        
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // Smooth countdown
        };
        _countdownTimer.Tick += OnCountdownTick;
        
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        
        // Position at top center of primary screen
        PositionWindow();
        
        // Set task name
        TaskNameText.Text = TaskName;
        
        // Initialize countdown
        _sessionStartTime = DateTime.Now;
        _sessionEndTime = _sessionStartTime.AddMinutes(DurationMinutes);
        UpdateTimerDisplay();
        
        // Start countdown
        _countdownTimer.Start();
        
        // Start pulse animation
        var pulse = (Storyboard)FindResource("PulseAnimation");
        pulse.Begin(this);
        
        // Fade in
        var fadeIn = (Storyboard)FindResource("FadeIn");
        fadeIn.Begin(this);
        
        // Register hotkey
        RegisterHotkey();
    }

    private void PositionWindow()
    {
        // Center horizontally, near top of screen
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - ActualWidth) / 2;
        Top = 20;
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        UpdateTimerDisplay();
        
        // Check if session ended
        if (DateTime.Now >= _sessionEndTime)
        {
            EndSession(completed: true);
        }
    }

    private void UpdateTimerDisplay()
    {
        var remaining = _sessionEndTime - DateTime.Now;
        
        if (remaining.TotalSeconds <= 0)
        {
            TimerText.Text = "00:00";
            return;
        }
        
        // Show MM:SS format
        TimerText.Text = remaining.ToString(@"mm\:ss");
        
        // Visual feedback when low on time
        if (remaining.TotalMinutes <= 1)
        {
            TimerText.Foreground = FindResource("WarningBrush") as System.Windows.Media.Brush;
        }
        else if (remaining.TotalSeconds <= 10)
        {
            TimerText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
        }
    }

    private void ExtendButton_Click(object sender, RoutedEventArgs e)
    {
        // Add 5 minutes
        _sessionEndTime = _sessionEndTime.AddMinutes(5);
        DurationMinutes += 5;
        UpdateTimerDisplay();
        
        // Reset timer color if it was warning
        TimerText.Foreground = FindResource("TextPrimaryBrush") as System.Windows.Media.Brush;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        EndSession(completed: false);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the pill
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    #region Hotkey

    private void RegisterHotkey()
    {
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WndProc);
        
        // Ctrl+Shift+Q to end session
        RegisterHotKey(_windowHandle, HOTKEY_END_SESSION, 
            MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_Q);
    }

    private void UnregisterHotkey()
    {
        UnregisterHotKey(_windowHandle, HOTKEY_END_SESSION);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_END_SESSION)
        {
            EndSession(completed: false);
            handled = true;
        }
        return IntPtr.Zero;
    }

    #endregion

    private void EndSession(bool completed)
    {
        _countdownTimer.Stop();
        
        var actualDuration = DateTime.Now - _sessionStartTime;
        
        SessionEnded?.Invoke(this, new FocusSessionResult
        {
            TaskName = TaskName,
            PlannedDuration = TimeSpan.FromMinutes(DurationMinutes),
            ActualDuration = actualDuration,
            Completed = completed,
            StartTime = _sessionStartTime,
            EndTime = DateTime.Now
        });
        
        // Fade out then close
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _countdownTimer.Stop();
        UnregisterHotkey();
    }
}

public class FocusSessionResult : EventArgs
{
    public required string TaskName { get; init; }
    public required TimeSpan PlannedDuration { get; init; }
    public required TimeSpan ActualDuration { get; init; }
    public required bool Completed { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
}
