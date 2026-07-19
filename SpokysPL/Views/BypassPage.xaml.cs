using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SpokysProjectLightning.Models;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class BypassPage : UserControl
    {
        private readonly DataService _dataService;
        private readonly ManifestService _manifestService;
        private Dictionary<string, Dictionary<string, GameInfo>> _bypassData = new();
        private List<GameInfo> _allGames = new();
        private string _currentFilter = "All";

        public BypassPage()
        {
            InitializeComponent();
            _dataService = new DataService();
            _manifestService = new ManifestService();
            Loaded += BypassPage_Loaded;
        }

        private void BypassPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                _bypassData = _dataService.LoadBypassData();

                _allGames.Clear();
                foreach (var category in _bypassData)
                {
                    foreach (var game in category.Value)
                    {
                        game.Value.Category = category.Key;
                        game.Value.HeaderUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{game.Key}/header.jpg";
                        _allGames.Add(game.Value);
                    }
                }

                // Add data-fix games
                var fixGames = _dataService.LoadDataFix();
                foreach (var game in fixGames)
                {
                    game.Category = "Fixes";
                    if (!string.IsNullOrEmpty(game.AppId) && int.TryParse(game.AppId, out _))
                        game.HeaderUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{game.AppId}/header.jpg";
                    _allGames.Add(game);
                }

                ApplyFilter("All");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading bypass data: {ex.Message}");
            }
        }

        private void ApplyFilter(string category)
        {
            _currentFilter = category;
            var filtered = category == "All"
                ? _allGames
                : _allGames.Where(g => g.Category == category).ToList();

            GamesList.ItemsSource = filtered;
        }

        private void FilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string category)
            {
                ApplyFilter(category);
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search games...")
                SearchBox.Text = "";
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                PerformSearch();
        }

        private void PerformSearch()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == "Search games...")
            {
                ApplyFilter(_currentFilter);
                return;
            }

            var filtered = _allGames
                .Where(g => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || g.AppId.Contains(query))
                .ToList();

            GamesList.ItemsSource = filtered;
        }

        private void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is GameInfo game)
            {
                var msg = $"🎮 {game.Name}\n" +
                          $"App ID: {game.AppId}\n" +
                          $"Fix: {game.FixName}\n" +
                          $"Category: {game.Category}\n" +
                          $"Steam Launch: {(game.LaunchSteam ? "Yes" : "No")}\n" +
                          $"EXE Launch: {(game.LaunchExe ? "Yes" : "No")}";
                if (game.RequiredPrograms.Count > 0)
                    msg += $"\nRequired: {string.Join(", ", game.RequiredPrograms)}";
                if (!string.IsNullOrEmpty(game.Comments))
                    msg += $"\n\nNotes: {game.Comments}";
                MessageBox.Show(msg, "Game Details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LaunchSteam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string appId)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"steam://rungameid/{appId}",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void LaunchExe_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string appId)
            {
                var game = _allGames.FirstOrDefault(g => g.AppId == appId);
                if (game != null)
                {
                    try
                    {
                        var steamPath = SteamService.FindSteamPath();
                        if (steamPath != null)
                        {
                            var steamapps = Path.Combine(steamPath, "steamapps", "common");
                            if (Directory.Exists(steamapps))
                            {
                                var dirs = Directory.GetDirectories(steamapps);
                                var match = dirs.FirstOrDefault(d =>
                                    Path.GetFileName(d).Contains(game.Name, StringComparison.OrdinalIgnoreCase) ||
                                    game.Name.Contains(Path.GetFileName(d), StringComparison.OrdinalIgnoreCase));
                                if (match != null)
                                {
                                    var exes = Directory.GetFiles(match, "*.exe", SearchOption.TopDirectoryOnly);
                                    if (exes.Length > 0)
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = exes[0],
                                            UseShellExecute = true,
                                            WorkingDirectory = match
                                        });
                                        return;
                                    }
                                }
                            }
                        }
                        MessageBox.Show("Could not find the game executable.\nCheck your Steam library folder.", "Not Found",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch { }
                }
            }
        }

        private async void ApplyFix_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string appId)
            {
                var game = _allGames.FirstOrDefault(g => g.AppId == appId);
                if (game != null)
                {
                    btn.IsEnabled = false;
                    btn.Content = "⏳ Installing...";

                    try
                    {
                        var result = await _manifestService.InstallGameManifestAsync(appId, game.Name);

                        if (result.Success)
                        {
                            string message = $"✅ {game.Name} - Fix Applied Successfully!\n\n" +
                                             $"Installed {result.ManifestsInstalled} manifest files\n" +
                                             $"AppManifest: {result.AppManifestPath}\n" +
                                             $"Lua Script: {result.LuaPath}\n\n" +
                                             $"Instructions:\n- " +
                                             $"Launch via Steam: {(game.LaunchSteam ? "Yes" : "No")}\n" +
                                             $"Launch via EXE: {(game.LaunchExe ? "Yes" : "No")}\n\n";

                            if (game.RequiredPrograms.Count > 0)
                                message += $"Required: {string.Join(", ", game.RequiredPrograms)}\n\n";

                            if (game.Errors.Count > 0)
                                message += $"Notes:\n- {string.Join("\n- ", game.Errors.Take(3))}";

                            MessageBox.Show(message, game.FixName, MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"❌ Installation failed:\n{result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"❌ Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        btn.IsEnabled = true;
                        btn.Content = "🔧 Apply Fix";
                    }
                }
            }
        }
    }
}

