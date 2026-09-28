using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;

namespace FitGirlLauncher.Views;

/// <summary>
/// Manual Steam match picker: lists the top store-search results for a game's title
/// and applies the one the user clicks. Applies through SteamDetailStore.ApplyTo —
/// the same persistence path the automatic lookup uses, so the fix sticks (survives
/// restarts and re-scans). Code-behind only, same style as ScreenshotWindow.
/// </summary>
public class SteamMatchWindow : Window
{
    private readonly GameEntry _game;
    private readonly SteamDetailStore _steamStore;
    private readonly SteamStoreClient _client = new();
    private readonly StackPanel _resultsPanel = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _statusText;
    private readonly TextBox _searchBox;
    private readonly Button _searchButton;
    private bool _busy;

    public SteamMatchWindow(GameEntry game, SteamDetailStore steamStore)
    {
        _game = game;
        _steamStore = steamStore;

        Title = $"Fix Steam match — {game.CleanTitle}";
        Width = 540;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _searchBox = new TextBox
        {
            Text = game.CleanTitle,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _searchButton = new Button
        {
            Content = "Search",
            Width = 84,
            IsDefault = true
        };
        _searchButton.Click += async (_, _) => await SearchAsync(_searchBox.Text.Trim());

        var searchRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_searchButton, Dock.Right);
        searchRow.Children.Add(_searchButton);
        searchRow.Children.Add(_searchBox);

        _statusText = new TextBlock
        {
            Text = "Searching…",
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Steam search",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(searchRow);
        root.Children.Add(_statusText);
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
            Content = _resultsPanel
        });
        Content = root;

        // Start with the same term the automatic lookup used, so the user sees its
        // candidates immediately (plus the rest of Steam's list).
        Loaded += async (_, _) => await SearchAsync(game.CleanTitle);
    }

    private async Task SearchAsync(string term)
    {
        if (_busy || string.IsNullOrWhiteSpace(term))
            return;
        _busy = true;
        SetButtonsEnabled(false);
        _statusText.Text = $"Searching Steam for “{term}”…";
        try
        {
            var hits = await Task.Run(() => _client.SearchMultipleAsync(term));
            ShowResults(term, hits);
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private void ShowResults(string term, List<SteamSearchHit> hits)
    {
        _resultsPanel.Children.Clear();
        if (hits.Count == 0)
        {
            _statusText.Text = $"No results for “{term}” — try a shorter or differently-spelled term.";
            return;
        }

        _statusText.Text = hits.Count == 1
            ? "One result — click it to use it."
            : $"{hits.Count} results — click the right one to use it.";

        foreach (var hit in hits)
        {
            var button = new Button
            {
                Content = $"{hit.Name}  —  App ID {hit.AppId}",
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = hit
            };
            button.Click += OnHitClicked;
            _resultsPanel.Children.Add(button);
        }
    }

    private async void OnHitClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SteamSearchHit hit })
            return;
        SetButtonsEnabled(false);
        _statusText.Text = $"Fetching store details for “{hit.Name}”…";
        try
        {
            var details = await Task.Run(() => _client.GetAppDetailsAsync(hit.AppId));
            if (details is null)
            {
                // Steam has no store page for that App ID (delisted, etc.). Record the
                // App ID + name anyway — same shape as the automatic lookup's
                // NotOnSteam-with-hit result — and let the user pick again.
                _steamStore.ApplyTo(_game, new SteamDetailResult(
                    SteamLookupStatus.NotOnSteam, hit.AppId, hit.Name, null,
                    Array.Empty<string>(), Array.Empty<string>(), null));
                _statusText.Text = $"Steam has no store page for App ID {hit.AppId} — the ID was saved anyway. You can pick another.";
                SetButtonsEnabled(true);
                return;
            }

            _steamStore.ApplyTo(_game, new SteamDetailResult(
                SteamLookupStatus.Found,
                details.AppId,
                details.Name,
                details.ShortDescription,
                details.ScreenshotUrls,
                details.ScreenshotThumbUrls,
                null));
            Close();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Couldn't fetch details: {ex.Message}";
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _searchButton.IsEnabled = enabled;
        foreach (var child in _resultsPanel.Children.OfType<Button>())
            child.IsEnabled = enabled;
    }
}
