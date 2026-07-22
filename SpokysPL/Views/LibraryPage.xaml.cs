using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpokysProjectVercel.Models;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public partial class LibraryPage : UserControl
    {
        private readonly SteamService _steamService;
        private readonly DownloadService _downloader;
        private List<ManifestInfo> _recommendedGames = new();
        private bool _hasLoaded;

        public LibraryPage()
        {
            InitializeComponent();
            _steamService = new SteamService();
            _downloader = new DownloadService();
            Loaded += LibraryPage_Loaded;
        }

        private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_hasLoaded)
            {
                _hasLoaded = true;
                await LoadRecommendations();
            }
        }

        private async System.Threading.Tasks.Task LoadRecommendations()
        {
            StatusText.Text = "🔄 Fetching recommended games from Steam...";
            try
            {
                _recommendedGames = await _steamService.GetRecommendedPaidGamesAsync();
                if (_recommendedGames.Count > 0)
                {
                    GamesList.ItemsSource = _recommendedGames;
                    NoGamesPanel.Visibility = Visibility.Collapsed;
                    StatusText.Text = $"✅ Loaded {_recommendedGames.Count} recommended games";
                }
                else
                {
                    StatusText.Text = "⚠️ Could not fetch recommendations. Try refreshing.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search Steam library for a game...")
                SearchBox.Text = "";
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await PerformSearch();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
            => await PerformSearch();

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
            => await LoadRecommendations();

        private async System.Threading.Tasks.Task PerformSearch()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == "Search Steam library for a game...") return;

            StatusText.Text = $"🔍 Searching for '{query}' on Steam...";
            try
            {
                var results = await _steamService.SearchGameAsync(query);
                if (results.Count > 0)
                {
                    GamesList.ItemsSource = results;
                    NoGamesPanel.Visibility = Visibility.Collapsed;
                    StatusText.Text = $"✅ Found {results.Count} results for '{query}'";
                }
                else
                {
                    StatusText.Text = $"❌ No results found for '{query}'";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
            }
        }

        private async void GetManifest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string appId)
            {
                StatusText.Text = $"📥 Getting manifest for App ID {appId} from fares.top...";
                try
                {
                    var manifestUrl = await _steamService.GetManifestFromFaresTopAsync(appId);
                    if (!string.IsNullOrEmpty(manifestUrl))
                    {
                        // If the URL is the fares.top page itself (not a direct download), open in browser
                        if (manifestUrl.StartsWith("https://fares.top/app/"))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = manifestUrl,
                                UseShellExecute = true
                            });
                            StatusText.Text = $"🌍 Opened {manifestUrl} in browser — look for the download link there";
                        }
                        else
                        {
                            // Direct manifest file URL — download it
                            var result = await _downloader.DownloadFileAsync(manifestUrl, $"manifest_{appId}.txt");
                            if (!string.IsNullOrEmpty(result))
                                StatusText.Text = $"✅ Manifest downloaded to {result}";
                            else
                                StatusText.Text = "❌ Could not download manifest. Try opening fares.top in browser.";
                        }
                    }
                    else
                    {
                        var url = $"https://fares.top/app/{appId}";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        StatusText.Text = $"🌍 Opened {url} in browser — look for the manifest link there";
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"❌ Error: {ex.Message}";
                }
            }
        }

        private void OpenFaresTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    StatusText.Text = $"🌐 Opening {url} in browser...";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"❌ Error: {ex.Message}";
                }
            }
        }
    }
}

