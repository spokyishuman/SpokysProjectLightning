using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class SettingsPage : UserControl
    {
        private readonly DataService _data;
        private readonly ManifestService _manifest;
        private string? _lastTheme;

        private static readonly Dictionary<string, int> ThemeIndex = new()
        {
            ["Dark"] = 0, ["Emerald"] = 1, ["Midnight Blue"] = 2, ["Royal Purple"] = 3, ["Light"] = 4
        };

        public SettingsPage()
        {
            InitializeComponent();
            _data = new DataService();
            _manifest = new ManifestService();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = _data.LoadSettings();
            if (!string.IsNullOrEmpty(settings.SteamPath) && Directory.Exists(settings.SteamPath))
            {
                SteamPathDisplay.Text = settings.SteamPath;
                ToolsStatus.Text = "✅ Steam path loaded";
            }
            else
            {
                var detected = SteamService.FindSteamPath();
                if (detected != null)
                {
                    SteamPathDisplay.Text = detected;
                    settings.SteamPath = detected;
                    _data.SaveSettings(settings);
                }
            }

            // Theme selector
            _lastTheme = settings.Theme;
            if (ThemeIndex.TryGetValue(settings.Theme, out var idx))
                ThemeSelector.SelectedIndex = idx;

            // Download path
            var dlPath = settings.DownloadPath;
            if (string.IsNullOrEmpty(dlPath))
                dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SpokysPL");
            DownloadPathDisplay.Text = dlPath;

            var steamPath = SteamService.FindSteamPath();
            if (steamPath != null)
            {
                var toolsInstalled =
                    Directory.Exists(Path.Combine(steamPath, "config", "stplug-in")) ||
                    Directory.Exists(Path.Combine(steamPath, "config", "lua"));
                if (toolsInstalled)
                {
                    InstallToolsBtn.Content = "✅ Installed";
                }
            }

            // Backdrop state
            if (settings.UseVideoBackdrop && !string.IsNullOrEmpty(settings.BackdropPath))
            {
                ToggleBackdropBtn.Content = "🎬 Disable";
                BackdropStatus.Text = $"✅ Playing: {Path.GetFileName(settings.BackdropPath)}";
                if (File.Exists(settings.BackdropPath))
                    VideoPaletteService.ApplyFromVideo(settings.BackdropPath);
            }
            else
            {
                ToggleBackdropBtn.Content = "🎬 Enable";
            }

            // Build color options
            BuildColorPanel(settings.CustomColors);

            // API Keys
            TmdbApiKeyBox.Text = settings.TmdbApiKey;
            OmdbApiKeyBox.Text = settings.OmdbApiKey;
            SteamDaddyApiKeyBox.Text = settings.SteamDaddyApiKey;

            // Update URL
            if (!string.IsNullOrEmpty(settings.UpdateUrl))
            {
                UpdateService.UpdateCheckUrl = settings.UpdateUrl;
                UpdateUrlBox.Text = settings.UpdateUrl;
            }
            else
            {
                UpdateUrlBox.Text = UpdateService.UpdateCheckUrl;
            }
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag != _lastTheme)
            {
                _lastTheme = tag;
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.ApplyTheme(tag);
                    ThemeStatus.Text = $"✅ Theme changed to {tag}";
                }
            }
        }

        private void BrowseDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new FolderBrowserDialog("Select download folder")
            { Owner = Window.GetWindow(this) };
            if (fbd.ShowDialog() == true && fbd.SelectedPath != null)
            {
                DownloadPathDisplay.Text = fbd.SelectedPath;
                SaveDownloadPath(fbd.SelectedPath);
            }
        }

        private void ResetDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SpokysPL");
            DownloadPathDisplay.Text = defaultPath;
            SaveDownloadPath(defaultPath);
        }

        private void SaveDownloadPath(string path)
        {
            var settings = _data.LoadSettings();
            settings.DownloadPath = path;
            _data.SaveSettings(settings);
            DownloadPathStatus.Text = $"✅ Download folder set to {path}";
        }

        private readonly Dictionary<string, Border> _colorSwatches = new();
        private readonly Dictionary<string, TextBox> _colorInputs = new();

        private void BuildColorPanel(Dictionary<string, string> overrides)
        {
            ColorOptionsPanel.Children.Clear();
            _colorSwatches.Clear();
            _colorInputs.Clear();

            string? lastCategory = null;
            foreach (var opt in ColorCustomizationService.GetOptions())
            {
                if (opt.Category != lastCategory)
                {
                    lastCategory = opt.Category;
                    // Resolve brushes defensively: resource may not be available at this time
                    var textSecondaryBrush = TryFindResource("TextSecondaryBrush") as Brush ?? System.Windows.Media.Brushes.Gray;
                    ColorOptionsPanel.Children.Add(new TextBlock
                    {
                        Text = opt.Category,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = textSecondaryBrush,
                        Margin = new Thickness(0, 8, 0, 4)
                    });
                }

                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0x1F, 0x19, 0x00)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock
                {
                    Text = opt.DisplayName,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? System.Windows.Media.Brushes.Black
                };
                Grid.SetColumn(nameBlock, 0);

                var currentHex = overrides.TryGetValue(opt.Key, out var h) ? h : opt.DefaultHex;
                opt.CurrentHex = currentHex;

                var swatch = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1),
                    BorderBrush = TryFindResource("BorderBrush") as Brush ?? System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Click to pick color",
                    Tag = opt.Key
                };
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentHex)); } catch { swatch.Background = new SolidColorBrush(System.Windows.Media.Colors.Gray); }
                swatch.MouseLeftButtonUp += (_, _) => OpenColorPicker(opt.Key);
                Grid.SetColumn(swatch, 1);
                _colorSwatches[opt.Key] = swatch;

                var hexBox = new TextBox
                {
                    Text = currentHex,
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Padding = new Thickness(6, 4, 6, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var key = opt.Key;
                hexBox.TextChanged += (_, _) => UpdateSwatch(key, hexBox.Text);
                Grid.SetColumn(hexBox, 2);
                _colorInputs[opt.Key] = hexBox;

                var resetBtn = new Button
                {
                    Content = "↺",
                    Width = 26,
                    Height = 26,
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Tag = opt.Key
                };
                // Use SetResourceReference so style resolution is deferred and no invalid cast occurs
                resetBtn.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButton");
                resetBtn.Click += ResetColor_Click;
                Grid.SetColumn(resetBtn, 3);

                grid.Children.Add(nameBlock);
                grid.Children.Add(swatch);
                grid.Children.Add(hexBox);
                grid.Children.Add(resetBtn);
                row.Child = grid;
                ColorOptionsPanel.Children.Add(row);
            }
        }

        private void UpdateSwatch(string key, string hex)
        {
            if (_colorSwatches.TryGetValue(key, out var swatch))
            {
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { }
            }
        }

        private void OpenColorPicker(string key)
        {
            if (!_colorInputs.TryGetValue(key, out var box)) return;
            var dialog = new ColorPickerDialog(box.Text)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                box.Text = dialog.SelectedHex;
                UpdateSwatch(key, dialog.SelectedHex);
            }
        }

        private void ResetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
            {
                var defaultHex = ColorCustomizationService.GetDefaultHex(key);
                if (_colorInputs.TryGetValue(key, out var box))
                    box.Text = defaultHex;
                UpdateSwatch(key, defaultHex);
            }
        }

        private void ApplyColors_Click(object sender, RoutedEventArgs e)
        {
            var overrides = new Dictionary<string, string>();
            foreach (var (key, box) in _colorInputs)
            {
                var hex = box.Text.Trim();
                if (hex != ColorCustomizationService.GetDefaultHex(key))
                    overrides[key] = hex;
            }

            ColorCustomizationService.ApplyCustomColors(overrides);

            var settings = _data.LoadSettings();
            settings.CustomColors = overrides;
            _data.SaveSettings(settings);

            ColorStatus.Text = $"✅ Applied {overrides.Count} color override(s)";
        }

        private void ResetAllColors_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (key, box) in _colorInputs)
            {
                var def = ColorCustomizationService.GetDefaultHex(key);
                box.Text = def;
                UpdateSwatch(key, def);
            }
            ApplyColors_Click(sender, e);
        }

        private void DetectSteam_Click(object sender, RoutedEventArgs e)
        {
            var path = SteamService.FindSteamPath();
            if (path != null)
            {
                SteamPathDisplay.Text = path;
                ToolsStatus.Text = "✅ Steam detected";
            }
            else
            {
                SteamPathDisplay.Text = "❌ Steam not found";
                ToolsStatus.Text = "⚠️ Could not detect Steam. Browse manually.";
            }
        }

        private void BrowseSteamPath_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new FolderBrowserDialog("Select Steam installation folder")
            { Owner = Window.GetWindow(this) };
            if (fbd.ShowDialog() == true && fbd.SelectedPath != null)
            {
                SteamPathDisplay.Text = fbd.SelectedPath;
                ToolsStatus.Text = "📍 Path selected";
            }
        }

        private void SaveSteamPath_Click(object sender, RoutedEventArgs e)
        {
            var path = SteamPathDisplay.Text;
            if (path == "Detecting Steam..." || path == "❌ Steam not found")
            {
                ToolsStatus.Text = "⚠️ Set a valid Steam path first";
                return;
            }
            var settings = _data.LoadSettings();
            settings.SteamPath = path;
            _data.SaveSettings(settings);
            ToolsStatus.Text = "✅ Steam path saved";
        }

        private async void DownloadInstaller_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            UpdateStatus.Text = "⏳ Finding latest installer...";

            try
            {
                var svc = new UpdateService();
                var update = await svc.CheckForUpdatesAsync();
                if (update == null)
                {
                    UpdateStatus.Text = "❌ Could not find installer. Check your connection.";
                    return;
                }

                // Find the setup exe from the GitHub release
                string? setupUrl = null;
                try
                {
                    var json = await new HttpClient().GetStringAsync(UpdateService.UpdateCheckUrl);
                    var gh = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                    if (gh?.assets != null)
                    {
                        foreach (var asset in gh.assets)
                        {
                            string name = asset.name;
                            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                setupUrl = asset.browser_download_url;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(setupUrl))
                {
                    UpdateStatus.Text = "❌ No installer found in the latest release. Upload a Setup.exe to GitHub releases.";
                    return;
                }

                UpdateStatus.Text = $"⬇ Downloading installer (v{update.Version})...";

                var destDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpokysPL", "Downloads");
                Directory.CreateDirectory(destDir);
                var destPath = Path.Combine(destDir, $"SpokysPL-Setup-v{update.Version}.exe");

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                var bytes = await http.GetByteArrayAsync(setupUrl);
                await File.WriteAllBytesAsync(destPath, bytes);

                UpdateStatus.Text = $"✅ Installer saved. Launching...";
                Process.Start(new ProcessStartInfo { FileName = destPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = $"❌ Error: {ex.Message}";
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            UpdateStatus.Text = "Checking for updates...";

            try
            {
                var svc = new UpdateService();
                var update = await svc.CheckForUpdatesAsync();
                if (update != null)
                {
                    UpdateStatus.Text = $"✅ v{update.Version} available! Downloading...";

                    var progress = new Progress<double>(p =>
                    {
                        Dispatcher.BeginInvoke(() =>
                            UpdateStatus.Text = $"⬇ Downloading... {p:F0}%");
                    });

                    var zipPath = await svc.DownloadUpdateAsync(update.DownloadUrl, progress);
                    if (zipPath != null)
                    {
                        UpdateStatus.Text = "📦 Installing update...";
                        if (svc.InstallUpdate(zipPath))
                        {
                            UpdateStatus.Text = "✅ Update applied. Restarting...";
                            await Task.Delay(1000);
                            Application.Current.Shutdown();
                        }
                        else
                        {
                            UpdateStatus.Text = "❌ Install failed. Try manual download.";
                        }
                    }
                    else
                    {
                        UpdateStatus.Text = "❌ Download failed. Try again later.";
                    }
                }
                else
                {
                    UpdateStatus.Text = "✅ You're up to date!";
                }
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = $"❌ Error: {ex.Message}";
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void InstallTools_Click(object sender, RoutedEventArgs e)
        {
            var steamPath = SteamService.FindSteamPath();
            if (steamPath == null)
            {
                MessageBox.Show("Steam not found. Set the Steam path first.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var depotCache = Path.Combine(steamPath, "config", "depotcache");
                Directory.CreateDirectory(depotCache);

                var created = new List<string>();
                var distinctDirs = new[] { "lua", "stplug-in" };
                foreach (var subDir in distinctDirs)
                {
                    var luaDir = Path.Combine(steamPath, "config", subDir);
                    Directory.CreateDirectory(luaDir);
                    created.Add(luaDir);

                    var loaderFile = subDir == "stplug-in" ? "cloudredirect.lua" : "steamtools.lua";
                    var loaderPath = Path.Combine(luaDir, loaderFile);
                    if (!File.Exists(loaderPath))
                    {
                        var displayName = subDir == "stplug-in" ? "CloudRedirect" : "SteamTools";
                        File.WriteAllText(loaderPath,
                            $"-- {displayName} Loader\n" +
                            "-- Generated by Spoky's Project Lightning\n" +
                            "return {\n" +
                            $"  name = '{displayName}',\n" +
                            "  loadOrder = 1,\n" +
                            "}\n");
                    }
                }

                var dirList = string.Join(" and ", created.Select(p => $"config\\{Path.GetFileName(p)}"));
                ToolsStatus.Text = $"✅ Tools ready for all modes (depotcache, {dirList})";
                InstallToolsBtn.Content = "✅ Installed";
            }
            catch (Exception ex)
            {
                ToolsStatus.Text = $"❌ Error: {ex.Message}";
            }
        }

        private void RemoveAllGames_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Remove all installed games?\nThis will delete appmanifest files, LUA scripts, and depot manifests for every game.",
                "Remove All Games", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var steamPath = SteamService.FindSteamPath();
            if (steamPath == null)
            {
                ToolsStatus.Text = "❌ Steam path not found";
                return;
            }

            try
            {
                var games = _manifest.GetInstalledGames(steamPath);
                if (games.Count == 0)
                {
                    ToolsStatus.Text = "ℹ️ No installed games to remove";
                    return;
                }

                int removed = 0;
                foreach (var game in games)
                {
                    if (_manifest.UninstallGameManifest(game.AppId, steamPath))
                        removed++;
                }

                ToolsStatus.Text = $"🗑️ Removed {removed} game(s)";
            }
            catch (Exception ex)
            {
                ToolsStatus.Text = $"❌ Error: {ex.Message}";
            }
        }

        private void ToggleBackdrop_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                if (vm.IsBackdropVisible)
                {
                    vm.SetBackdrop("");
                    ToggleBackdropBtn.Content = "🎬 Enable";
                    BackdropStatus.Text = "⏹️ Video background disabled";
                }
                else
                {
                    var appDir = AppContext.BaseDirectory;
                    var defaultVideo = !string.IsNullOrEmpty(appDir) ? Path.Combine(appDir, "background.mp4") : "";
                    if (!string.IsNullOrEmpty(defaultVideo) && File.Exists(defaultVideo))
                    {
                        vm.SetBackdrop(defaultVideo);
                        ToggleBackdropBtn.Content = "🎬 Disable";
                        BackdropStatus.Text = "✅ Video background enabled";
                        VideoPaletteService.ApplyFromVideo(defaultVideo);
                    }
                    else
                    {
                        BrowseBackdrop_Click(sender, e);
                    }
                }
            }
        }

        private void ScanAppFolder_Click(object sender, RoutedEventArgs e)
        {
            var appDir = AppContext.BaseDirectory;
            if (appDir == null) return;
            var files = Directory.GetFiles(appDir, "*.mp4").Concat(Directory.GetFiles(appDir, "*.webm")).ToList();
            if (files.Count == 0)
            {
                BackdropStatus.Text = "ℹ️ No video files found in app folder";
                AppVideosPanel.Visibility = Visibility.Collapsed;
                return;
            }

            AppVideosPanel.Children.Clear();
            AppVideosPanel.Visibility = Visibility.Visible;
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var btn = new Button
                {
                    Content = $"🎬 {name}",
                    Style = (Style)FindResource("SecondaryButton"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 0, 4),
                    Tag = file
                };
                btn.Click += (s, args) =>
                {
                    if (s is Button b && b.Tag is string path)
                    {
                        if (DataContext is ViewModels.MainViewModel vm)
                        {
                            vm.SetBackdrop(path);
                            ToggleBackdropBtn.Content = "🎬 Disable";
                            BackdropStatus.Text = $"✅ Playing: {Path.GetFileName(path)}";
                            AppVideosPanel.Visibility = Visibility.Collapsed;
                            VideoPaletteService.ApplyFromVideo(path);
                        }
                    }
                };
                AppVideosPanel.Children.Add(btn);
            }
            BackdropStatus.Text = $"📁 {files.Count} video(s) found in app folder";
        }

        private void VideoCard_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)))
                    e.Effects = DragDropEffects.Copy;
                else
                    e.Effects = DragDropEffects.None;
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void VideoCard_DragLeave(object sender, DragEventArgs e) { }

        private void VideoCard_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var video = files.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));
                if (video != null && DataContext is ViewModels.MainViewModel vm)
                {
                    vm.SetBackdrop(video);
                    ToggleBackdropBtn.Content = "🎬 Disable";
                    BackdropStatus.Text = $"✅ Playing: {Path.GetFileName(video)}";
                    VideoPaletteService.ApplyFromVideo(video);
                }
            }
        }

        private void BrowseBackdrop_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video files (*.mp4;*.webm;*.mkv;*.avi;*.wmv;*.mov)|*.mp4;*.webm;*.mkv;*.avi;*.wmv;*.mov|All files (*.*)|*.*",
                Title = "Select background video"
            };
            if (dialog.ShowDialog() == true)
            {
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.SetBackdrop(dialog.FileName);
                    ToggleBackdropBtn.Content = "🎬 Disable";
                    BackdropStatus.Text = $"✅ Playing: {Path.GetFileName(dialog.FileName)}";
                    VideoPaletteService.ApplyFromVideo(dialog.FileName);
                }
            }
        }

        private void SaveTmdbKey_Click(object sender, RoutedEventArgs e)
        {
            var settings = _data.LoadSettings();
            settings.TmdbApiKey = TmdbApiKeyBox.Text.Trim();
            _data.SaveSettings(settings);
            ApiKeyStatus.Text = "✅ TMDB API key saved";
        }

        private void SaveOmdbKey_Click(object sender, RoutedEventArgs e)
        {
            var settings = _data.LoadSettings();
            settings.OmdbApiKey = OmdbApiKeyBox.Text.Trim();
            _data.SaveSettings(settings);
            ApiKeyStatus.Text = "✅ OMDb API key saved";
        }

        private void SaveSteamDaddyKey_Click(object sender, RoutedEventArgs e)
        {
            var settings = _data.LoadSettings();
            settings.SteamDaddyApiKey = SteamDaddyApiKeyBox.Text.Trim();
            _data.SaveSettings(settings);
            ApiKeyStatus.Text = "✅ SteamDaddy API key saved (20 fetches/day)";
        }

        private void SaveUpdateUrl_Click(object sender, RoutedEventArgs e)
        {
            var settings = _data.LoadSettings();
            settings.UpdateUrl = UpdateUrlBox.Text.Trim();
            _data.SaveSettings(settings);
            UpdateService.UpdateCheckUrl = settings.UpdateUrl;
            ((Button)sender).Content = "✅ Saved";
        }

        private void ResetUpdateUrl_Click(object sender, RoutedEventArgs e)
        {
            var defaultUrl = "https://api.github.com/repos/spokyishuman/SpokysProjectLightning/releases/latest";
            UpdateUrlBox.Text = defaultUrl;
            var settings = _data.LoadSettings();
            settings.UpdateUrl = defaultUrl;
            _data.SaveSettings(settings);
            UpdateService.UpdateCheckUrl = defaultUrl;
            UpdateStatus.Text = "Update URL reset to default";
        }

        private void UninstallTools_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Remove all LUA scripts and manifests?\nThis won't affect installed games.",
                "Uninstall Tools", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var steamPath = SteamService.FindSteamPath();
            if (steamPath != null)
            {
                try
                {
                    foreach (var subDir in new[] { "lua", "stplug-in" })
                    {
                        var luaDir = Path.Combine(steamPath, "config", subDir);
                        if (Directory.Exists(luaDir))
                            Directory.Delete(luaDir, true);
                    }
                    ToolsStatus.Text = "🗑️ Tools removed";
                    InstallToolsBtn.Content = "📥 Install";
                }
                catch (Exception ex)
                {
                    ToolsStatus.Text = $"❌ Error: {ex.Message}";
                }
            }
        }
    }
}

