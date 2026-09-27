using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FitGirlLauncher.Services;

namespace FitGirlLauncher.Views;

/// <summary>
/// Small corner toast shown when a background check finds an update.
/// Code-behind only (no XAML), like <see cref="ScreenshotWindow"/>. Never steals
/// focus (ShowWithoutActivation) and auto-dismisses after ~20 seconds.
/// </summary>
public class UpdateToastWindow : Window
{
    private readonly string _newVersion;
    private readonly string _currentVersion;
    private readonly DispatcherTimer _timer;

    public UpdateToastWindow(string currentVersion, string newVersion)
    {
        _newVersion = newVersion;
        _currentVersion = currentVersion;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // never steal focus from what the user is doing
        Topmost = true;
        Width = 360;
        SizeToContent = SizeToContent.Height;

        Content = BuildCard();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        Loaded += (_, _) =>
        {
            // Dock to the bottom-right of the working area (stays clear of the taskbar).
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
            _timer.Start();
        };
    }

    private UIElement BuildCard()
    {
        var title = new TextBlock
        {
            Text = "FitGirl Launcher has an update",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        };

        var body = new TextBlock
        {
            Text = $"Version {_newVersion} is downloaded and ready (you're on {_currentVersion}).",
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };

        var later = new Button
        {
            Content = "Later",
            MinWidth = 70,
        };
        later.Click += (_, _) => Close();

        var restart = new Button
        {
            Content = "Restart to update",
            MinWidth = 120,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
        };
        restart.Click += (_, _) =>
        {
            if (!UpdateService.Instance.ApplyPendingUpdate())
                MessageBox.Show(
                    "The update couldn't be applied. Check update.log in your AppData FitGirlLauncher folder for details.",
                    "FitGirl Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(later);
        row.Children.Add(restart);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(title);
        content.Children.Add(body);
        content.Children.Add(row);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(8), // leaves room for the drop shadow
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 },
            Child = content,
        };
    }
}
