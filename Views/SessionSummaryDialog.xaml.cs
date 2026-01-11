using System.Windows;
using MonkMode.Models;
using MonkMode.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace MonkMode.Views;

/// <summary>
/// Dialog shown after a focus session ends.
/// Collects user feedback and optionally provides AI coaching.
/// </summary>
public partial class SessionSummaryDialog : Window
{
    private readonly SessionLog _session;
    private readonly DatabaseService _database;
    private readonly AiCoachService _aiCoach;
    private int _selectedRating;
    private string? _coachResponse;

    public SessionSummaryDialog(SessionEndedEventArgs sessionData)
    {
        InitializeComponent();

        _database = new DatabaseService();
        _aiCoach = new AiCoachService();

        // Create session log
        _session = new SessionLog
        {
            TaskName = sessionData.TaskName,
            StartTime = DateTime.Now - sessionData.Duration,
            EndTime = DateTime.Now,
            IntensityLevel = sessionData.IntensityLevel,
            InterventionCount = sessionData.InterventionCount
        };

        // Save initial session
        _session.Id = _database.SaveSession(_session);

        // Update UI
        PopulateSessionData(sessionData);
    }

    private void PopulateSessionData(SessionEndedEventArgs data)
    {
        TaskNameText.Text = data.TaskName;
        DurationText.Text = data.Duration.ToString(@"hh\:mm\:ss");
        
        IntensityText.Text = data.IntensityLevel switch
        {
            1 => "Flow",
            2 => "Deep Work",
            3 => "Blackout",
            _ => "Unknown"
        };

        IntensityText.Foreground = data.IntensityLevel switch
        {
            1 => FindResource("FocusBlueBrush") as Brush,
            2 => FindResource("EmberBrush") as Brush,
            3 => FindResource("SoftWhiteBrush") as Brush,
            _ => FindResource("MutedTextBrush") as Brush
        };

        InterventionText.Text = data.InterventionCount.ToString();
        InterventionText.Foreground = data.InterventionCount == 0
            ? FindResource("SuccessBrush") as Brush
            : FindResource("WarningBrush") as Brush;

        // Show coaching button only if AI is configured
        GetCoachingButton.Visibility = _aiCoach.IsConfigured 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    #region Star Rating

    private void StarRating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int rating))
        {
            _selectedRating = rating;
            UpdateStarDisplay(rating);
        }
    }

    private void UpdateStarDisplay(int rating)
    {
        var stars = new[] { Star1, Star2, Star3, Star4, Star5 };
        var emberBrush = FindResource("EmberBrush") as Brush;
        var dimBrush = FindResource("DimTextBrush") as Brush;

        for (int i = 0; i < stars.Length; i++)
        {
            stars[i].Foreground = i < rating ? emberBrush : dimBrush;
        }
    }

    #endregion

    #region AI Coaching

    private async void GetCoaching_Click(object sender, RoutedEventArgs e)
    {
        GetCoachingButton.IsEnabled = false;
        LoadingText.Visibility = Visibility.Visible;

        try
        {
            // Update session with user notes before analysis
            _session.UserNotes = UserNotesInput.Text;
            _session.FlowRating = _selectedRating;

            _coachResponse = await _aiCoach.AnalyzeSessionAsync(_session, UserNotesInput.Text);

            // Display response
            CoachResponseText.Text = _coachResponse;
            CoachResponsePanel.Visibility = Visibility.Visible;

            // Update session in database
            _database.UpdateSessionRating(_session.Id, _selectedRating, UserNotesInput.Text, _coachResponse);
        }
        catch (Exception ex)
        {
            CoachResponseText.Text = $"Unable to get coaching: {ex.Message}";
            CoachResponsePanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingText.Visibility = Visibility.Collapsed;
            GetCoachingButton.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        // Save final rating and notes
        if (_selectedRating > 0 || !string.IsNullOrWhiteSpace(UserNotesInput.Text))
        {
            _database.UpdateSessionRating(
                _session.Id, 
                _selectedRating, 
                UserNotesInput.Text,
                _coachResponse);
        }

        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _database.Dispose();
        _aiCoach.Dispose();
        base.OnClosed(e);
    }
}
