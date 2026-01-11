using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Animation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace MonkMode.Views;

/// <summary>
/// Command palette for quick task entry.
/// Format: "Task description [duration in minutes]"
/// Examples: "Write report 25", "Deep work 90", "Quick email" (defaults to 25)
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private const int DefaultDurationMinutes = 25;
    
    public event EventHandler<FocusSessionRequest>? SessionRequested;

    public CommandPaletteWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the input
        CommandInput.Focus();
        
        // Play entrance animation
        var fadeIn = (Storyboard)FindResource("FadeIn");
        fadeIn.Begin(this);
    }

    private void CommandInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Show/hide placeholder
        Placeholder.Visibility = string.IsNullOrEmpty(CommandInput.Text) 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseWithAnimation();
                e.Handled = true;
                break;
                
            case Key.Enter:
                ProcessCommand();
                e.Handled = true;
                break;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Close when clicking outside
        CloseWithAnimation();
    }

    private void ProcessCommand()
    {
        string input = CommandInput.Text.Trim();
        
        if (string.IsNullOrEmpty(input))
            return;

        var (taskName, duration) = ParseInput(input);
        
        if (string.IsNullOrEmpty(taskName))
            return;

        // Fire event with parsed session request
        SessionRequested?.Invoke(this, new FocusSessionRequest
        {
            TaskName = taskName,
            DurationMinutes = duration
        });

        Close();
    }

    /// <summary>
    /// Parse input like "Write report 25" into task name and duration.
    /// </summary>
    private static (string taskName, int durationMinutes) ParseInput(string input)
    {
        // Pattern: everything except trailing number is the task name
        // "Write report 25" → task="Write report", duration=25
        // "Deep work" → task="Deep work", duration=25 (default)
        
        var match = Regex.Match(input, @"^(.+?)\s+(\d+)\s*$");
        
        if (match.Success)
        {
            string task = match.Groups[1].Value.Trim();
            int duration = int.Parse(match.Groups[2].Value);
            
            // Clamp duration to reasonable values
            duration = Math.Clamp(duration, 1, 480); // 1 min to 8 hours
            
            return (task, duration);
        }
        
        // No duration specified, use default
        return (input.Trim(), DefaultDurationMinutes);
    }

    private void CloseWithAnimation()
    {
        // Quick fade out
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}

public class FocusSessionRequest : EventArgs
{
    public required string TaskName { get; init; }
    public required int DurationMinutes { get; init; }
}
