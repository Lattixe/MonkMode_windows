using System.Security.Principal;
using System.Windows;
using MonkMode.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace MonkMode.Views;

/// <summary>
/// Main dashboard window for Monk Mode.
/// Allows configuration of focus session parameters before entering the overlay.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SystemBlockerService _systemBlocker;

    public MainWindow()
    {
        InitializeComponent();

        _systemBlocker = new SystemBlockerService();
        Application.Current.Properties["SystemBlocker"] = _systemBlocker;

        // Check admin status
        CheckAdminPrivileges();
    }

    private void CheckAdminPrivileges()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

        if (!isAdmin)
        {
            AdminWarning.Visibility = Visibility.Visible;
        }
    }

    #region Window Chrome Handlers

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize (disabled for this app)
            return;
        }
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    #endregion

    #region Session Management

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // Gather configuration
        string taskName = TaskNameInput.Text.Trim();
        if (string.IsNullOrEmpty(taskName))
        {
            taskName = "Focus Session";
        }

        int intensityLevel = GetSelectedIntensity();

        // Parse blocked apps
        var blockedApps = BlockedAppsInput.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // Parse blocked sites
        var blockedSites = BlockedSitesInput.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // Configure system blocker
        _systemBlocker.SetBlockedProcesses(blockedApps);
        _systemBlocker.SetBlockedDomains(blockedSites);

        // Hide taskbar
        _systemBlocker.HideTaskbar();

        // Start blocking
        try
        {
            _systemBlocker.StartBlocking();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Administrator Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            
            // Continue anyway - just won't have DNS blocking
        }

        // Create and show overlay
        var overlayWindow = new OverlayWindow
        {
            TaskName = taskName,
            IntensityLevel = intensityLevel,
            BlockedProcesses = blockedApps,
            BlockedDomains = blockedSites
        };

        overlayWindow.SessionEnded += OnSessionEnded;

        // Hide main window
        Hide();

        // Show overlay
        overlayWindow.Show();
    }

    private int GetSelectedIntensity()
    {
        if (FlowMode.IsChecked == true) return 1;
        if (DeepMode.IsChecked == true) return 2;
        if (BlackoutMode.IsChecked == true) return 3;
        return 2; // Default to Deep
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Stop blocking
            _systemBlocker.StopBlocking();
            _systemBlocker.RestoreTaskbar();

            // Show session summary dialog
            var summaryDialog = new SessionSummaryDialog(e)
            {
                Owner = this
            };
            summaryDialog.ShowDialog();

            // Show main window again
            Show();
            Activate();
        });
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        // Ensure cleanup
        _systemBlocker.Dispose();
        base.OnClosed(e);
    }
}
