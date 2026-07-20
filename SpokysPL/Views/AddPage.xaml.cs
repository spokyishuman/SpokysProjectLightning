using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class AddPage : UserControl
    {
        private readonly SteamToolsGamesService _steamTools = new();
        private readonly ManifestService _manifestService = new();
        private readonly RyuuFixesService _fixesService = new();
        private List<GameItem> _searchResults = new();
        private bool _installing;
        private readonly DispatcherTimer _searchTimer = new();

        public AddPage()
        {
            InitializeComponent();
            Loaded += AddPage_Loaded;
            _searchTimer.Interval = TimeSpan.FromMilliseconds(400);
            _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await PerformSearch(); };
        }

        private async void AddPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var steam = new SteamService();
                var paid = await steam.GetRecommendedPaidGamesAsync();
                if (paid.Count > 0)
                {
                    var mapped = paid.Select(g => new GameItem
                    {
                        Title = g.Name,
                        AppId = g.AppId,
                        Tag = "Steam",
                        CoverUrl = g.ImageUrl,
                        Downloads = 0
                    }).ToList();

                    TopSellersGrid.ItemsSource = mapped.Take(6).ToList();
                    NewReleasesGrid.ItemsSource = mapped.Skip(6).Take(6).ToList();
                    return;
                }
            }
            catch { }

            var samples = GenerateSampleGames();
            TopSellersGrid.ItemsSource = samples.Take(4).ToList();
            NewReleasesGrid.ItemsSource = samples.Skip(4).Take(4).ToList();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ResultsSection == null) return;
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == "Search games...")
            {
                _searchTimer.Stop();
                ResultsSection.Visibility = Visibility.Collapsed;
                DefaultSection.Visibility = Visibility.Visible;
                SearchSpinner.Visibility = Visibility.Collapsed;
                return;
            }
            ResultsSection.Visibility = Visibility.Visible;
            DefaultSection.Visibility = Visibility.Collapsed;
            ResultsCountText.Text = "Searching...";
            SearchSpinner.Visibility = Visibility.Visible;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private async Task PerformSearch()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == "Search games...") return;
            try
            {
                var results = await _steamTools.SearchGamesAsync(query);
                _searchResults = results.Select(r => new GameItem
                {
                    Title = r.name,
                    AppId = r.id,
                    Tag = "Steam",
                    CoverUrl = r.image,
                    Downloads = 0
                }).ToList();

                ResultsCountText.Text = $"Results ({_searchResults.Count})";
                if (_searchResults.Count > 0)
                {
                    ResultsGrid.ItemsSource = _searchResults;
                    ResultsGrid.Visibility = Visibility.Visible;
                    NoResultsText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ResultsGrid.ItemsSource = null;
                    ResultsGrid.Visibility = Visibility.Collapsed;
                    NoResultsText.Visibility = Visibility.Visible;
                    NoResultsText.Text = $"No games match \"{query}\".";
                }
            }
            catch (Exception ex)
            {
                ResultsCountText.Text = "Search failed";
                NoResultsText.Visibility = Visibility.Visible;
                NoResultsText.Text = $"Search error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AddPage search error: {ex}");
            }
            finally
            {
                SearchSpinner.Visibility = Visibility.Collapsed;
            }
        }

        private void AppIdBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (AppIdBox.Text == "Enter App ID...")
                AppIdBox.Text = "";
        }

        private void AppIdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                AddGameBtn_Click(sender, e);
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search games...")
                SearchBox.Text = "";
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
                SearchBox.Text = "Search games...";
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = "Search games...";
                ResultsSection.Visibility = Visibility.Collapsed;
                DefaultSection.Visibility = Visibility.Visible;
                SearchSpinner.Visibility = Visibility.Collapsed;
            }
        }

        private async void SearchResult_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is GameItem game)
            {
                await InstallGame(game);
            }
        }

        private async Task InstallGame(GameItem game)
        {
            if (_installing) return;
            _installing = true;

            try
            {
                ToastService.Show($"📥 Queued {game.Title} for installation", "info");
                var result = await InstallationManager.Instance.EnqueueAsync(game.AppId, game.Title);
                if (result.Success)
                    ToastService.Show($"✅ {game.Title} - {result.ManifestsInstalled} manifests installed!", "success", 5000);
                else
                    ToastService.Show($"❌ {result.Message}", "error", 6000);
            }
            catch (Exception ex)
            {
                ToastService.Show($"❌ Error: {ex.Message}", "error", 6000);
            }
            finally
            {
                _installing = false;
            }
        }

        private async void AddGameBtn_Click(object sender, RoutedEventArgs e)
        {
            var input = AppIdBox.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                ToastService.Show("Enter an App ID or Steam URL", "warning");
                return;
            }

            var match = System.Text.RegularExpressions.Regex.Match(input, @"(?:store\.steampowered\.com/app/|steam://|app/)(\d+)");
            var appId = match.Success ? match.Groups[1].Value : input;

            if (!string.IsNullOrEmpty(appId) && _searchResults.FirstOrDefault(g => g.AppId == appId) is GameItem found)
            {
                await InstallGame(found);
                return;
            }

            if (!int.TryParse(appId, out _))
            {
                ToastService.Show("Enter a valid numeric App ID or Steam URL", "warning");
                return;
            }

            await InstallGame(new GameItem { Title = $"App {appId}", AppId = appId, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg" });
        }

        private async void ContextInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is GameItem game)
                await InstallGame(game);
        }

        private void ContextOpenSteam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
            {
                try { Process.Start(new ProcessStartInfo { FileName = $"https://store.steampowered.com/app/{appId}", UseShellExecute = true }); }
                catch { }
            }
        }

        private void ContextCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
            {
                try { Clipboard.SetText(appId); ToastService.Show($"📋 App ID {appId} copied", "success"); }
                catch { }
            }
        }

        private async void TopSeller_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is GameItem game)
                await InstallGame(game);
        }

        private async void NewRelease_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is GameItem game)
                await InstallGame(game);
        }

        private void BrowseFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Game files (*.lua;*.manifest;*.zip)|*.lua;*.manifest;*.zip|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                HandleFiles(dialog.FileNames);
            }
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = FindResource("PrimaryBrush") as Brush;
                DropZone.BorderThickness = new Thickness(2);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = FindResource("CardBorderBrush") as Brush;
            DropZone.BorderThickness = new Thickness(1);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = FindResource("CardBorderBrush") as Brush;
            DropZone.BorderThickness = new Thickness(1);

            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                HandleFiles(files);
            }
        }

        private void HandleFiles(string[] files)
        {
            ManifestPaths.EnsureDirs();
            int count = 0;
            foreach (var file in files)
            {
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                var fileName = System.IO.Path.GetFileName(file);

                switch (ext)
                {
                    case ".lua":
                        System.IO.File.Copy(file, System.IO.Path.Combine(ManifestPaths.LuaDir, fileName), true);
                        count++;
                        break;
                    case ".manifest":
                        System.IO.File.Copy(file, System.IO.Path.Combine(ManifestPaths.ManifestDir, fileName), true);
                        count++;
                        break;
                    case ".acf":
                        System.IO.File.Copy(file, System.IO.Path.Combine(ManifestPaths.ManifestDir, fileName), true);
                        count++;
                        break;
                }
            }

            if (count > 0)
                ToastService.Show($"✅ {count} file(s) saved to SteamDaddy data", "success");
            else
                ToastService.Show("No supported files found (.lua, .manifest, .acf)", "warning");
        }

        private static List<GameItem> GenerateSampleGames()
        {
            return new List<GameItem>
            {
                new() { Title = "Elden Ring", AppId = "1245620", Tag = "RPG", Downloads = 12500000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1245620/library_600x900.jpg" },
                new() { Title = "Baldur's Gate 3", AppId = "1086940", Tag = "RPG", Downloads = 9800000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1086940/library_600x900.jpg" },
                new() { Title = "Cyberpunk 2077", AppId = "1091500", Tag = "Action", Downloads = 15200000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1091500/library_600x900.jpg" },
                new() { Title = "Red Dead Redemption 2", AppId = "1174180", Tag = "Action", Downloads = 11000000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1174180/library_600x900.jpg" },
                new() { Title = "Palworld", AppId = "1623730", Tag = "Survival", Downloads = 8500000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1623730/library_600x900.jpg" },
                new() { Title = "Hogwarts Legacy", AppId = "990080", Tag = "RPG", Downloads = 7200000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/990080/library_600x900.jpg" },
                new() { Title = "Lethal Company", AppId = "1966720", Tag = "Horror", Downloads = 6400000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1966720/library_600x900.jpg" },
                new() { Title = "Stardew Valley", AppId = "413150", Tag = "Simulation", Downloads = 5800000, CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/413150/library_600x900.jpg" },
            };
        }
    }

    public class GameItem
    {
        public string Title { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public int Downloads { get; set; }
        public string CoverUrl { get; set; } = string.Empty;
    }
}

