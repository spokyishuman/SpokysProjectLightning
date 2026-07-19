using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpokysProjectLightning.Models;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class OnlineFixPage : UserControl
    {
        private readonly OnlineFixScraperService _scraper;
        private readonly DataService _dataService;
        private readonly FixInstallerService _installer;
        private List<OnlineFixGame> _allGames = new();
        private string _currentCategory = "All";
        private bool _hasLoaded;
        private CancellationTokenSource? _downloadCts;
        private string? _selectedGameDir;
        private static string DownloadPath => Path.Combine(DataService.GetDownloadPath(), "OnlineFixes");

        public OnlineFixPage()
        {
            InitializeComponent();
            _scraper = new OnlineFixScraperService();
            _dataService = new DataService();
            _installer = new FixInstallerService();
            Directory.CreateDirectory(DownloadPath);

            Loaded += OnlineFixPage_Loaded;
        }

        private async void OnlineFixPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_hasLoaded)
            {
                _hasLoaded = true;
                BuildCategoryButtons();
                await RefreshGames();
            }
        }

        private void BuildCategoryButtons()
        {
            CategoryPanel.Children.Clear();
            
            // Add "All" button first
            var allBtn = new Button
            {
                Content = "All",
                Tag = "All",
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 6, 12, 6),
                Style = (Style)FindResource("AccentButton")
            };
            allBtn.Click += CategoryBtn_Click;
            CategoryPanel.Children.Add(allBtn);

            foreach (var cat in OnlineFixScraperService.Categories)
            {
                var btn = new Button
                {
                    Content = char.ToUpper(cat[0]) + cat.Substring(1),
                    Tag = cat,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(12, 6, 12, 6),
                    Style = (Style)FindResource("SecondaryButton")
                };
                btn.Click += CategoryBtn_Click;
                CategoryPanel.Children.Add(btn);
            }
        }

        private async Task RefreshGames()
        {
            StatusText.Text = "🔄 Scraping Online-Fix.me... This may take a moment.";
            RefreshBtn.IsEnabled = false;
            ProgressBorder.Visibility = Visibility.Visible;
            try
            {
                _allGames = await _scraper.ScrapeAllAsync();
                ApplyFilter();
                StatusText.Text = $"✅ Loaded {_allGames.Count} online fixes";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
            }
            finally
            {
                RefreshBtn.IsEnabled = true;
                ProgressBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var filtered = _allGames;
            if (_currentCategory != "All")
            {
                filtered = _allGames.Where(g => g.Category.Equals(_currentCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            var searchText = SearchBox.Text.Trim();
            if (!string.IsNullOrEmpty(searchText) && searchText != "Search games...")
                filtered = filtered.Where(g => g.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            GamesList.ItemsSource = filtered;
            StatusText.Text = $"Showing {filtered.Count} of {_allGames.Count} games" +
                              (_currentCategory != "All" ? $" in {_currentCategory}" : "");
        }

        private void CategoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string category)
            {
                _currentCategory = category;
                foreach (Button child in CategoryPanel.Children)
                    child.Style = child.Tag as string == category
                        ? (Style)FindResource("AccentButton")
                        : (Style)FindResource("SecondaryButton");
                ApplyFilter();
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search games...") SearchBox.Text = "";
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Text = "Search games...";
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyFilter();
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e) => ApplyFilter();
        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshGames();

        // Add a fix from an online-fix.me URL
        private async void AddFix_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "Add Fix from Online-Fix.me",
                Width = 520,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = TryFindResource("BackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(15, 15, 25))
            };

            var panel = new StackPanel { Margin = new Thickness(20, 20, 20, 20) };
            var label = new TextBlock
            {
                Text = "Paste the online-fix.me game page URL:",
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            var urlBox = new TextBox
            {
                Text = "https://online-fix.me/games/...",
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                Background = TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45)),
                Padding = new Thickness(8, 8, 8, 8),
                FontSize = 13
            };
            var statusText = new TextBlock
            {
                Foreground = TryFindResource("SuccessBrush") as Brush ?? Brushes.LightGreen,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12
            };

            var addBtn = new Button
            {
                Content = "➕ Add to Database",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Background = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White
            };
            addBtn.Click += async (s, ev) =>
            {
                var url = urlBox.Text.Trim();
                if (string.IsNullOrEmpty(url) || !url.Contains("online-fix.me"))
                {
                    statusText.Text = "❌ Please enter a valid online-fix.me URL";
                    statusText.Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.OrangeRed;
                    return;
                }

                statusText.Text = "🔄 Fetching game details...";
                statusText.Foreground = TryFindResource("WarningBrush") as Brush ?? Brushes.LightYellow;

                try
                {
                    var game = await _scraper.GetGameDetailsAsync(url);
                    if (game != null)
                    {
                        _dataService.AddOnlineFixToDatabase(game);
                        statusText.Text = $"✅ Added '{game.Title}' to database!";
                        statusText.Foreground = TryFindResource("SuccessBrush") as Brush ?? Brushes.LightGreen;
                        _allGames.Add(game);
                        ApplyFilter();
                    }
                    else
                    {
                        statusText.Text = "❌ Could not fetch game details";
                        statusText.Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.OrangeRed;
                    }
                }
                catch (Exception ex)
                {
                    statusText.Text = $"❌ Error: {ex.Message}";
                    statusText.Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.OrangeRed;
                }
            };

            panel.Children.Add(label);
            panel.Children.Add(urlBox);
            panel.Children.Add(statusText);
            panel.Children.Add(addBtn);
            dialog.Content = panel;
            dialog.ShowDialog();
        }

        // Add fix to local database from the list
        private async void AddFixToDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is OnlineFixGame game)
            {
                StatusText.Text = $"📥 Adding '{game.Title}' to local database...";

                try
                {
                    if (game.DownloadLinks.Count == 0)
                    {
                        var details = await _scraper.GetGameDetailsAsync(game.Url);
                        if (details != null) game = details;
                    }

                    _dataService.AddOnlineFixToDatabase(game);
                    StatusText.Text = $"✅ '{game.Title}' added to database successfully!";

                    var idx = _allGames.FindIndex(g => g.Id == game.Id);
                    if (idx >= 0) _allGames[idx] = game;
                    ApplyFilter();
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"❌ Error adding fix: {ex.Message}";
                }
            }
        }

        // Game card click - show details
        private void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is OnlineFixGame game)
            {
                ShowDownloadOptions_Click(sender, e);
            }
        }

        // Show download options dialog with 4 sources
        private async void ShowDownloadOptions_Click(object sender, RoutedEventArgs e)
        {
            OnlineFixGame game;
            
            if (sender is Button btn && btn.Tag is OnlineFixGame g)
            {
                game = g;
            }
            else if (sender is Border border && border.Tag is OnlineFixGame b)
            {
                game = b;
            }
            else
            {
                return;
            }

            // Fetch fresh details if needed
            if (game.DownloadLinks.Count == 0)
            {
                StatusText.Text = "🔄 Fetching download links...";
                var details = await _scraper.GetGameDetailsAsync(game.Url);
                if (details != null) game = details;
            }

            // Resolve online-fix endpoints (hosters/drive/uploads) to real file-host links.
            // This is what fixes the 401 "User not recognized" on hoster pages — we warm
            // cookies and follow the chain to the actual download URL.
            StatusText.Text = "🔄 Resolving download sources...";
            try { game.DownloadLinks = await _scraper.ResolveAllDownloadsAsync(game); }
            catch { }

            // Auto-detect the game's install folder (user can override via Browse)
            if (string.IsNullOrEmpty(_selectedGameDir))
                _selectedGameDir = FixInstallerService.TryFindGameInstallDir(game.Title);

            var dialog = new Window
            {
                Title = $"Download Options - {game.Title}",
                Width = 600,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = TryFindResource("BackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(15, 15, 25)),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(20, 20, 20, 20) };
            
            // Game info header
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            var titleText = new TextBlock
            {
                Text = game.Title,
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold
            };
            var infoText = new TextBlock
            {
                Text = $"Category: {game.Category} | Size: {game.FileSize} | Password: {game.Password}",
                Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray,
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0)
            };
            headerPanel.Children.Add(titleText);
            headerPanel.Children.Add(infoText);
            panel.Children.Add(headerPanel);

            // Game folder picker (auto-detected, user can override)
            var folderPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var folderLabel = new TextBlock
            {
                Text = string.IsNullOrEmpty(_selectedGameDir) ? "📁 No game folder detected — fix will be extracted only" : $"📁 {_selectedGameDir}",
                Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360
            };
            var browseBtn = new Button
            {
                Content = "📂 Browse",
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(10, 0, 0, 0),
                Background = TryFindResource("SurfaceBrushLight") as Brush ?? new SolidColorBrush(Color.FromRgb(96, 125, 139)),
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White
            };
            browseBtn.Click += (s, ev) =>
            {
                try
                {
                    var fbd = new FolderBrowserDialog("Select the game's install folder")
                    {
                        Owner = Window.GetWindow(this)
                    };
                    if (fbd.ShowDialog() == true)
                    {
                        _selectedGameDir = fbd.SelectedPath;
                        folderLabel.Text = $"📁 {_selectedGameDir}";
                    }
                }
                catch { }
            };
            folderPanel.Children.Add(folderLabel);
            folderPanel.Children.Add(browseBtn);
            panel.Children.Add(folderPanel);

            // Download sources
            var sourcesLabel = new TextBlock
            {
                Text = "📥 Download Sources:",
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(sourcesLabel);

            var sourcesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

            if (game.DownloadLinks.Count == 0)
            {
                var noLinks = new TextBlock
                {
                    Text = "❌ No download links found. Click 'Open Page' to view on website.",
                    Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.OrangeRed,
                    FontSize = 12
                };
                sourcesPanel.Children.Add(noLinks);
            }
            else
            {
                foreach (var link in game.DownloadLinks)
                {
                    var linkPanel = new Border
                    {
                        Background = TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 12, 12, 12),
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    var linkGrid = new Grid();
                    linkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    linkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var linkInfo = new TextBlock
                    {
                        Text = $"{link.Label}",
                        Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(linkInfo, 0);

                    var isDirect = IsDirectArchiveLink(link.Url);
                    var downloadBtn = new Button
                    {
                        Content = isDirect ? "⬇️ Download & Install" : "🌐 Open in Browser",
                        Padding = new Thickness(15, 6, 15, 6),
                        Margin = new Thickness(10, 0, 0, 0),
                        Background = TryFindResource("SuccessBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                        Tag = link.Url,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    downloadBtn.Click += (s, ev) =>
                    {
                        var url = downloadBtn.Tag as string;
                        if (string.IsNullOrEmpty(url)) return;
                        dialog.Close();
                        if (isDirect)
                        {
                            DownloadAndInstallAsync(url, game);
                        }
                        else
                        {
                            // mega/gofile/mediafire/etc. can't be fetched by HttpClient —
                            // open in the user's browser, then they drag-drop the file onto Home to install.
                            OpenUrl(url);
                            StatusText.Text = "🌐 Opened in browser. Download the file, then drag & drop it onto the Home page to install the fix.";
                        }
                    };
                    Grid.SetColumn(downloadBtn, 1);

                    linkGrid.Children.Add(linkInfo);
                    linkGrid.Children.Add(downloadBtn);
                    linkPanel.Child = linkGrid;
                    sourcesPanel.Children.Add(linkPanel);
                }
            }
            panel.Children.Add(sourcesPanel);

            // Action buttons
            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            
            var addDbBtn = new Button
            {
                Content = "📥 Add to Database",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Background = TryFindResource("InfoBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White
            };
            addDbBtn.Click += (s, ev) =>
            {
                _dataService.AddOnlineFixToDatabase(game);
                var idx = _allGames.FindIndex(g => g.Id == game.Id);
                if (idx >= 0) _allGames[idx] = game;
                ApplyFilter();
                dialog.Close();
                StatusText.Text = $"✅ '{game.Title}' added to database!";
            };

            var openPageBtn = new Button
            {
                Content = "🌐 Open Page",
                Padding = new Thickness(20, 8, 20, 8),
                Background = TryFindResource("SurfaceBrushLight") as Brush ?? new SolidColorBrush(Color.FromRgb(96, 125, 139)),
                Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White
            };
            openPageBtn.Click += (s, ev) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = game.Url, UseShellExecute = true });
                }
                catch { }
                dialog.Close();
            };

            actionsPanel.Children.Add(addDbBtn);
            actionsPanel.Children.Add(openPageBtn);
            panel.Children.Add(actionsPanel);

            dialog.Content = panel;
            dialog.ShowDialog();
        }

        private async void DownloadGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is OnlineFixGame game)
            {
                if (game.DownloadLinks.Count == 0)
                {
                    StatusText.Text = "🔄 Fetching download links...";
                    var details = await _scraper.GetGameDetailsAsync(game.Url);
                    if (details != null) game = details;
                }

                if (game.DownloadLinks.Count > 0)
                {
                    var link = game.DownloadLinks.First();
                    DownloadAndInstallAsync(link.Url, game);
                }
                else
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = game.Url, UseShellExecute = true });
                        StatusText.Text = "🌐 Opened page in browser";
                    }
                    catch
                    {
                        StatusText.Text = "❌ Could not open page";
                    }
                }
            }
        }

        /// <summary>True when the URL points at a file we can download + extract directly
        /// (online-fix's own file host, or any .rar/.zip/.7z/.001). Hoster sites like mega/gofile
        /// require a real browser session and are opened there instead.</summary>
        private static bool IsDirectArchiveLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".001", StringComparison.OrdinalIgnoreCase))
                return true;
            return url.Contains("uploads.online-fix.me", StringComparison.OrdinalIgnoreCase);
        }

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }

        /// <summary>
        /// Full download → extract → install pipeline for a chosen source link.
        /// Reports phases (Downloading / Extracting / Installing) to the download panel.
        /// </summary>
        private async void DownloadAndInstallAsync(string url, OnlineFixGame game)
        {
            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();

            DownloadPanel.Visibility = Visibility.Visible;
            DownloadFileName.Text = $"📥 {game.Title}";
            DownloadPercent.Text = "0%";
            DownloadSpeed.Text = "Starting...";
            DownloadSize.Text = "";
            DownloadProgressBar.Width = 0;

            var progress = new Progress<FixInstallerService.InstallProgress>(p =>
            {
                DownloadSpeed.Text = p.Status;
                DownloadPercent.Text = $"{p.Percent}%";
                DownloadProgressBar.Width = 400.0 * (p.Percent / 100.0);
                DownloadSize.Text = p.TotalBytes > 0
                    ? $"{FormatSize(p.BytesReceived)} / {FormatSize(p.TotalBytes)}"
                    : FormatSize(p.BytesReceived);
            });

            try
            {
                var result = await _installer.DownloadExtractInstallAsync(
                    url, game.Title, _selectedGameDir, game.Password, progress, _downloadCts.Token);

                DownloadProgressBar.Width = 400;
                DownloadPercent.Text = result.Success ? "100%" : "❌";
                DownloadSpeed.Text = result.Success ? "✅ Done — ready to play" : "❌ Failed";
                StatusText.Text = result.Message;
            }
            catch (OperationCanceledException)
            {
                DownloadSpeed.Text = "❌ Cancelled";
            }
            catch (Exception ex)
            {
                DownloadSpeed.Text = $"❌ Error: {ex.Message}";
            }
        }

        private void CloseDownloadPanel_Click(object sender, RoutedEventArgs e)
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
        }

        private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", DownloadPath);
            }
            catch { }
        }

        private void OpenPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    StatusText.Text = "🌐 Opened page in browser";
                }
                catch
                {
                    StatusText.Text = "❌ Could not open page";
                }
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
                fileName = fileName.Replace(c, '_');
            return fileName;
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F1} {suffixes[i]}";
        }
    }
}

