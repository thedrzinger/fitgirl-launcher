using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FitGirlLauncher.Views;

/// <summary>Borders a Steam screenshot at full resolution on a black backdrop.
/// Loads in the background with an indeterminate progress bar until the image lands.</summary>
public class ScreenshotWindow : Window
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public ScreenshotWindow(string url)
    {
        Title = "Screenshot";
        Width = 1040;
        Height = 640;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var image = new Image
        {
            Stretch = Stretch.Uniform
        };
        var spinner = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 160,
            Height = 6,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var host = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        host.Children.Add(spinner);
        host.Children.Add(image);
        Content = host;

        Loaded += async (_, _) =>
        {
            var loaded = await LoadAsync(url);
            if (loaded is null)
            {
                spinner.Visibility = Visibility.Collapsed;
                host.Children.Add(new TextBlock
                {
                    Text = "Couldn't load this screenshot.",
                    Foreground = Brushes.Gray
                });
                return;
            }
            image.Source = loaded;
            spinner.Visibility = Visibility.Collapsed;
        };
    }

    // A BitmapImage pointed at a web Uri cannot be Frozen (its HTTP stream stays
    // open) — download the bytes and decode from a stream instead.
    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            return await Task.Run(() =>
            {
                var image = new BitmapImage();
                image.BeginInit();
                // (No IgnoreImageCache — see DecodeImage note in GameDetailViewModel.)
                image.CacheOption = BitmapCacheOption.OnLoad;
                using var stream = new MemoryStream(bytes);
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            });
        }
        catch
        {
            return null;
        }
    }
}
