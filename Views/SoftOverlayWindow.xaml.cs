using System.Windows;
using System.Windows.Media.Animation;

namespace MonkMode.Views;

/// <summary>
/// A soft vignette overlay that darkens screen edges without harsh cutouts.
/// Creates ambient focus without jarring visual effects.
/// </summary>
public partial class SoftOverlayWindow : Window
{
    public SoftOverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Fade in smoothly
        var fadeIn = (Storyboard)FindResource("FadeIn");
        fadeIn.Begin(this);
    }

    /// <summary>
    /// Set the intensity of the vignette (0.0 to 1.0)
    /// </summary>
    public void SetIntensity(double intensity)
    {
        // Adjust the vignette opacity based on intensity
        intensity = Math.Clamp(intensity, 0, 1);
        VignetteOverlay.Opacity = intensity;
    }

    /// <summary>
    /// Fade out and close the overlay.
    /// </summary>
    public void FadeOutAndClose()
    {
        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Completed += (_, _) => Close();
        fadeOut.Begin(this);
    }
}
