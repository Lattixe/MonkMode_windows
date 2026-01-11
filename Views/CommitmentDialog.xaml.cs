using System.Windows;
using System.Windows.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace MonkMode.Views;

/// <summary>
/// Commitment dialog - requires user to type "I give up" to exit early.
/// Creates psychological friction against distraction.
/// </summary>
public partial class CommitmentDialog : Window
{
    private const string RequiredPhrase = "i give up";
    
    /// <summary>
    /// True if user confirmed they want to give up.
    /// </summary>
    public bool UserGaveUp { get; private set; }

    public CommitmentDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Ensure the text box gets keyboard focus
        ConfirmInput.Focusable = true;
        ConfirmInput.IsEnabled = true;
        ConfirmInput.Focus();
        System.Windows.Input.Keyboard.Focus(ConfirmInput);
    }

    /// <summary>
    /// Set the time remaining display.
    /// </summary>
    public void SetTimeRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
        {
            TimeRemainingText.Text = remaining.ToString(@"h\:mm\:ss");
        }
        else
        {
            TimeRemainingText.Text = remaining.ToString(@"mm\:ss");
        }
    }

    private void ConfirmInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Enable give up button only when phrase matches exactly
        string input = ConfirmInput.Text.Trim().ToLowerInvariant();
        GiveUpButton.IsEnabled = input == RequiredPhrase;
    }

    private void GiveUp_Click(object sender, RoutedEventArgs e)
    {
        UserGaveUp = true;
        DialogResult = true;
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        UserGaveUp = false;
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Escape = back to focus
            UserGaveUp = false;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && GiveUpButton.IsEnabled)
        {
            // Enter when phrase matches = give up
            UserGaveUp = true;
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }
}
