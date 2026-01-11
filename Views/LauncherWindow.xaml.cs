using System.Text.RegularExpressions;
using System.Windows;
using MonkMode.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Application = System.Windows.Application;

namespace MonkMode.Views;

/// <summary>
/// Simple launcher window for starting focus sessions.
/// Type: "Task name [minutes]" and press Enter or click arrow.
/// </summary>
public partial class LauncherWindow : Window
{
    private const int DefaultDurationMinutes = 25;
    private bool _isStartingSession; // Flag to prevent hiding when starting a session
    
    public event EventHandler<FocusSessionRequest>? SessionRequested;

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TaskInput.Focus();
        Deactivated += Window_Deactivated;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Hide when clicking away (Spotlight-style)
        // But don't hide if we're starting a session (picker will open)
        if (!_isStartingSession && IsVisible)
        {
            // Small delay to check if focus moved to another MonkMode window
            var timer = new System.Windows.Threading.DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(100) 
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                
                // Check if any other MonkMode window is active
                bool otherMonkModeWindowActive = false;
                foreach (Window window in Application.Current.Windows)
                {
                    if (window != this && window.IsActive && window.GetType().Namespace == "MonkMode.Views")
                    {
                        otherMonkModeWindowActive = true;
                        break;
                    }
                }
                
                // Only hide if no other MonkMode window became active
                if (!otherMonkModeWindowActive && !IsActive)
                {
                    Hide();
                }
            };
            timer.Start();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Hide launcher (Spotlight-style - Escape closes it)
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && StartButton.IsEnabled)
        {
            StartSession();
            e.Handled = true;
        }
    }

    private void TaskInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrWhiteSpace(TaskInput.Text);
        Placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = hasText;
        
        // Show duration badge if input ends with a number
        var match = Regex.Match(TaskInput.Text, @"\s+(\d+)\s*$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int mins))
        {
            mins = Math.Clamp(mins, 1, 480);
            DurationBadge.Visibility = Visibility.Visible;
            DurationText.Text = $"{mins}m";
        }
        else if (hasText)
        {
            // Show default duration
            DurationBadge.Visibility = Visibility.Visible;
            DurationText.Text = "25m";
        }
        else
        {
            DurationBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        StartSession();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Hide instead of close - app stays running in background
        // User can bring it back with Ctrl+Shift+Space
        Hide();
    }
    
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Prevent actual close - just hide
        e.Cancel = true;
        Hide();
    }

    private void StartSession()
    {
        string input = TaskInput.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (taskName, duration) = ParseInput(input);
        
        // Clear input for next time
        TaskInput.Clear();
        
        // Set flag to prevent hiding when picker opens
        _isStartingSession = true;
        
        // Hide launcher
        Hide();

        // Fire event
        SessionRequested?.Invoke(this, new FocusSessionRequest
        {
            TaskName = taskName,
            DurationMinutes = duration
        });
        
        // Reset flag after a delay (picker should be open by then)
        var timer = new System.Windows.Threading.DispatcherTimer 
        { 
            Interval = TimeSpan.FromMilliseconds(500) 
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            _isStartingSession = false;
        };
        timer.Start();
    }

    private static (string taskName, int durationMinutes) ParseInput(string input)
    {
        // Pattern: "Task name 25" → task="Task name", duration=25
        var match = Regex.Match(input, @"^(.+?)\s+(\d+)\s*$");
        
        if (match.Success)
        {
            string task = match.Groups[1].Value.Trim();
            int duration = int.Parse(match.Groups[2].Value);
            duration = Math.Clamp(duration, 1, 480);
            return (task, duration);
        }
        
        return (input.Trim(), DefaultDurationMinutes);
    }

    public void BringToFront()
    {
        WindowState = WindowState.Normal;
        Topmost = true; // Always on top when shown (Spotlight-style)
        Show(); // Ensure it's visible
        Activate();
        TaskInput.Focus();
    }
}
