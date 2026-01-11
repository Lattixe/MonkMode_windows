using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MonkMode.Views;

/// <summary>
/// Clean, minimal session completion dialog.
/// </summary>
public partial class SessionCompleteWindow : Window
{
    public SessionCompleteWindow()
    {
        InitializeComponent();
    }

    public string TaskName
    {
        get => TaskNameText.Text;
        set => TaskNameText.Text = value;
    }

    public string Duration
    {
        get => DurationText.Text;
        set => DurationText.Text = value;
    }

    public bool WasCompleted
    {
        set
        {
            if (value)
            {
                TitleText.Text = "Session Complete";
                StatusIcon.Text = "●";
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
            }
            else
            {
                TitleText.Text = "Session Ended";
                StatusIcon.Text = "●";
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)); // Zinc-500
            }
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Space)
        {
            Close();
            e.Handled = true;
        }
    }
}
