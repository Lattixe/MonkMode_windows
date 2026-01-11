using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MonkMode.Views;

/// <summary>
/// Floating End button that's always on top and clickable.
/// Small, subtle X button to end focus session.
/// </summary>
public partial class FloatingEndButton : Window
{
    public event EventHandler? EndClicked;

    public FloatingEndButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position in top-right corner, below the workspace control bar
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = screenWidth - Width - 20;
        Top = 60; // Just below the control bar
        
        Debug.WriteLine("[FloatingEnd] Button loaded and positioned");
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the button
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void EndButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[FloatingEnd] END BUTTON CLICKED!");
        e.Handled = true;
        
        try
        {
            if (EndClicked != null)
            {
                EndClicked.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Debug.WriteLine("[FloatingEnd] No EndClicked handler attached!");
                // Failsafe - try to end the session anyway
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is FocusWorkspaceWindow workspace)
                    {
                        workspace.ForceEnd();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FloatingEnd] Error invoking EndClicked: {ex.Message}");
        }
    }
}
