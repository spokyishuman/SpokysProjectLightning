using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class OpenSteamToolPage : UserControl
    {
        private readonly OpenSteamToolService _ost;
        private bool _loaded;

        public OpenSteamToolPage()
        {
            InitializeComponent();
            _ost = new OpenSteamToolService();
            Loaded += async (_, _) =>
            {
                if (!_loaded) { _loaded = true; await RefreshStatusAsync(); }
            };
        }

        private async Task RefreshStatusAsync()
        {
            RefreshBtn.IsEnabled = false;
            try
            {
                var (latestVer, releaseUrl, _, _) = await _ost.CheckLatestReleaseAsync();
                var installed = _ost.IsInstalled();
                var currentVer = _ost.GetInstalledVersion();

                VersionText.Text = latestVer != "0.0.0" ? $"v{latestVer}" : "offline";
                LatestVersion = latestVer;

                if (installed && currentVer != null)
                {
                    var hasUpdate = _ost.HasUpdateAvailable();
                    StatusIcon.Text = "✅";
                    StatusTitle.Text = $"OpenSteamTool v{currentVer} installed";
                    StatusSubtitle.Text = $"Latest: v{latestVer} | Steam: {_ost.GetSteamPath()}";
                    InstallBtn.Content = hasUpdate ? "⬇ Update Available" : "✅ Reinstall";
                    InstalledText.Text = $"✅ v{currentVer} installed";
                    InstalledBadge.Visibility = Visibility.Visible;
                    UninstallBtn.IsEnabled = true;

                    if (hasUpdate)
                    {
                        PatternText.Text = $"⬆ Update to v{latestVer} available";
                        PatternBadge.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PatternBadge.Visibility = Visibility.Collapsed;
                    }
                }
                else if (installed)
                {
                    StatusIcon.Text = "⚠️";
                    StatusTitle.Text = "OpenSteamTool DLLs found (unknown version)";
                    StatusSubtitle.Text = "Version file not found. Reinstall to track updates.";
                    InstallBtn.Content = "⬇ Reinstall";
                    InstalledText.Text = "⚠️ Unknown version";
                    InstalledBadge.Visibility = Visibility.Visible;
                    PatternBadge.Visibility = Visibility.Collapsed;
                    UninstallBtn.IsEnabled = true;
                }
                else
                {
                    StatusIcon.Text = "❌";
                    StatusTitle.Text = "OpenSteamTool not installed";
                    StatusSubtitle.Text = "Place DLLs in Steam root or click Install";
                    InstallBtn.Content = "⬇ Install";
                    InstalledBadge.Visibility = Visibility.Collapsed;
                    PatternBadge.Visibility = Visibility.Collapsed;
                    UninstallBtn.IsEnabled = false;
                }

                var luaCount = _ost.GetLuaConfigs().Count;
                LuaCountText.Text = $"📜 {luaCount} Lua config(s)";
                LuaCountBadge.Visibility = Visibility.Visible;
                RefreshLogList();
            }
            finally
            {
                RefreshBtn.IsEnabled = true;
            }
        }

        private string? LatestVersion { get; set; }

        private async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            InstallBtn.IsEnabled = false;
            StatusSubtitle.Text = "Downloading OpenSteamTool...";
            try
            {
                var success = await _ost.DownloadAndInstallAsync();
                if (success)
                {
                    ToastService.Show("✅ OpenSteamTool installed/updated successfully!", "success");
                }
                else
                {
                    ToastService.Show("❌ Failed to install OpenSteamTool", "error");
                }
            }
            catch (Exception ex)
            {
                ToastService.Show($"❌ Error: {ex.Message}", "error");
            }
            finally
            {
                InstallBtn.IsEnabled = true;
                await RefreshStatusAsync();
            }
        }

        private void UninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Remove OpenSteamTool DLLs from Steam folder?\n(dwmapi.dll, xinput1_4.dll, OpenSteamTool.dll)",
                "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var steamPath = _ost.GetSteamPath();
            if (steamPath == null) return;

            var files = new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll", "opensteamtool.version" };
            int removed = 0;
            foreach (var f in files)
            {
                var path = Path.Combine(steamPath, f);
                if (File.Exists(path)) { File.Delete(path); removed++; }
            }

            ToastService.Show($"🗑 Removed {removed} file(s)", "info");
            _ = RefreshStatusAsync();
        }

        private void OpenSteamDirBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = _ost.GetSteamPath();
            if (path != null) Process.Start("explorer.exe", path);
        }

        private void OpenLuaDirBtn_Click(object sender, RoutedEventArgs e)
        {
            var dir = _ost.GetLuaDir();
            if (!string.IsNullOrEmpty(dir)) Process.Start("explorer.exe", dir);
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        // --- Lua Tab ---
        private void RefreshLuaList()
        {
            var configs = _ost.GetLuaConfigs();
            LuaListBox.ItemsSource = configs;
            if (configs.Count == 0)
                LuaEditor.Text = "(no Lua configs yet — click +New Lua to create one)";
        }

        private void SetActiveTab(Button active, Button? tab1, Button? tab2)
        {
            active.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryBrush");
            if (tab1 != null) tab1.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            if (tab2 != null) tab2.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        }

        private async void TabLua_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentLuaEditAsync();
            SetActiveTab(TabLua, TabToml, TabLogs);
            LuaPanel.Visibility = Visibility.Visible;
            TomlPanel.Visibility = Visibility.Collapsed;
            LogsPanel.Visibility = Visibility.Collapsed;
            RefreshLuaList();
        }

        private async void TabToml_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentLuaEditAsync();
            SetActiveTab(TabToml, TabLua, TabLogs);
            LuaPanel.Visibility = Visibility.Collapsed;
            TomlPanel.Visibility = Visibility.Visible;
            LogsPanel.Visibility = Visibility.Collapsed;
            LoadTomlEditor();
        }

        private async void TabLogs_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentLuaEditAsync();
            SetActiveTab(TabLogs, TabLua, TabToml);
            LuaPanel.Visibility = Visibility.Collapsed;
            TomlPanel.Visibility = Visibility.Collapsed;
            LogsPanel.Visibility = Visibility.Visible;
            RefreshLogList();
        }

        private async void LuaListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Save current edits before switching
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is LuaConfigFile prev)
            {
                await _ost.SaveLuaConfigAsync(prev.FileName, LuaEditor.Text);
            }

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is LuaConfigFile config)
            {
                LuaEditor.Text = config.Content;
                DeleteLuaBtn.IsEnabled = true;
            }
            else if (LuaListBox.SelectedItem is LuaConfigFile selected)
            {
                LuaEditor.Text = selected.Content;
                DeleteLuaBtn.IsEnabled = true;
            }
            else
            {
                LuaEditor.Text = "(select a Lua config to view/edit)";
                DeleteLuaBtn.IsEnabled = false;
            }
        }

        private async void NewLuaBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "New Lua Config",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 16) };

            var lblAppId = new TextBlock
            {
                Text = "Steam App ID:",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var appIdBox = new TextBox
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50)),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var lblDepot = new TextBlock
            {
                Text = "Depot Key (optional):",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var depotBox = new TextBox
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50)),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var lblToken = new TextBlock
            {
                Text = "Access Token (optional):",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var tokenBox = new TextBox
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50)),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var lblManifest = new TextBlock
            {
                Text = "Manifest ID (optional):",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var manifestBox = new TextBox
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50)),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var lblFileName = new TextBlock
            {
                Text = "File name (e.g. mygame.lua):",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameBox = new TextBox
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50)),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var previewBox = new TextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                IsReadOnly = true,
                Foreground = System.Windows.Media.Brushes.LimeGreen,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 30)),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                Height = 80,
                Margin = new Thickness(0, 0, 0, 8),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            void UpdatePreview()
            {
                var appId = appIdBox.Text.Trim();
                if (string.IsNullOrEmpty(appId)) { previewBox.Text = "(enter App ID)"; return; }
                nameBox.Text = $"{appId}.lua";
                previewBox.Text = _ost.GenerateLuaContent(
                    appId,
                    depotBox.Text.Trim(),
                    tokenBox.Text.Trim(),
                    manifestBox.Text.Trim()
                );
            }

            appIdBox.TextChanged += (_, _) => UpdatePreview();
            depotBox.TextChanged += (_, _) => UpdatePreview();
            tokenBox.TextChanged += (_, _) => UpdatePreview();
            manifestBox.TextChanged += (_, _) => UpdatePreview();

            var saveBtn = new Button
            {
                Content = "💾 Save Lua Config",
                Padding = new Thickness(14, 8, 14, 8),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = false
            };

            saveBtn.Click += async (_, _) =>
            {
                var fileName = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("File name must end with .lua", "Error");
                    return;
                }
                var content = previewBox.Text;
                await _ost.SaveLuaConfigAsync(fileName, content);
                ToastService.Show($"✅ Saved {fileName}", "success");
                RefreshLuaList();
                dialog.Close();
            };

            appIdBox.TextChanged += (_, _) =>
                saveBtn.IsEnabled = !string.IsNullOrEmpty(appIdBox.Text.Trim());

            panel.Children.Add(lblAppId);
            panel.Children.Add(appIdBox);
            panel.Children.Add(lblDepot);
            panel.Children.Add(depotBox);
            panel.Children.Add(lblToken);
            panel.Children.Add(tokenBox);
            panel.Children.Add(lblManifest);
            panel.Children.Add(manifestBox);
            panel.Children.Add(lblFileName);
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock
            {
                Text = "Preview:",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(previewBox);
            panel.Children.Add(saveBtn);
            dialog.Content = panel;
            dialog.ShowDialog();
        }

        private async Task SaveCurrentLuaEditAsync()
        {
            if (LuaListBox.SelectedItem is LuaConfigFile current)
            {
                await _ost.SaveLuaConfigAsync(current.FileName, LuaEditor.Text);
            }
        }

        private async void DeleteLuaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LuaListBox.SelectedItem is not LuaConfigFile config) return;
            var result = MessageBox.Show($"Delete {config.FileName}?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _ost.DeleteLuaConfig(config.FileName);
            ToastService.Show($"🗑 Deleted {config.FileName}", "info");
            LuaListBox.SelectedItem = null;
            RefreshLuaList();
            LuaEditor.Text = "(select a Lua config to view/edit)";
        }

        private async void BatchFromDbBtn_Click(object sender, RoutedEventArgs e)
        {
            var dataService = new DataService();
            var allData = dataService.LoadBypassData();
            var allGames = allData.SelectMany(kv => kv.Value.Values).ToList();
            var fixGames = dataService.LoadDataFix();

            var dbGames = allGames.Concat(fixGames)
                .Where(g => !string.IsNullOrEmpty(g.AppId) && g.AppId.All(char.IsDigit))
                .GroupBy(g => g.AppId)
                .Select(g => g.First())
                .ToList();

            var existing = _ost.GetLuaConfigs().Select(f =>
                Path.GetFileNameWithoutExtension(f.FileName)).ToHashSet();
            var newGames = dbGames.Where(g => !existing.Contains(g.AppId)).ToList();

            if (newGames.Count == 0)
            {
                MessageBox.Show("All database games already have Lua configs.", "Batch Generate");
                return;
            }

            var result = MessageBox.Show(
                $"Generate Lua configs for {newGames.Count} games from database?",
                "Batch Generate", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            int count = 0;
            foreach (var game in newGames)
            {
                var content = _ost.GenerateLuaContent(game.AppId);
                await _ost.SaveLuaConfigAsync($"{game.AppId}.lua", content);
                count++;
            }

            ToastService.Show($"✅ Generated {count} Lua config(s)", "success");
            RefreshLuaList();
        }

        // --- TOML Tab ---
        private void LoadTomlEditor()
        {
            var content = _ost.ReadTomlConfig();
            TomlEditor.Text = content ?? _ost.GetDefaultTomlContent();
        }

        private void LoadDefaultTomlBtn_Click(object sender, RoutedEventArgs e)
        {
            TomlEditor.Text = _ost.GetDefaultTomlContent();
        }

        private async void SaveTomlBtn_Click(object sender, RoutedEventArgs e)
        {
            var success = await _ost.SaveTomlConfigAsync(TomlEditor.Text);
            if (success)
                ToastService.Show("✅ opensteamtool.toml saved", "success");
            else
                ToastService.Show("❌ Failed to save config", "error");
        }

        // --- Logs Tab ---
        private void RefreshLogList()
        {
            var logs = _ost.GetLogFiles();
            LogListBox.ItemsSource = logs;
            if (logs.Count == 0)
                LogViewer.Text = "(no log files found — run Steam with debug build)";
        }

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogListBox.SelectedItem is OpenSteamToolLog log)
            {
                LogViewer.Text = _ost.ReadLogFile(log.Path);
            }
        }

        private void RefreshLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogList();
        }
    }
}
