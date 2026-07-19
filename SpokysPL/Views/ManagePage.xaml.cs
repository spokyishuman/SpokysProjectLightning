using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public class ManageGame
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public long SizeOnDisk { get; set; }
        public string SizeDisplay { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string InstallDir { get; set; } = "";
        public string ExePath { get; set; } = "";
    }

    public partial class ManagePage : UserControl
    {
        private const int PER_PAGE = 10;
        private readonly ManifestService _manifest = new();
        private readonly List<ManageGame> _allGames = new();
        private string _query = string.Empty;
        private int _page = 1;
        private readonly DispatcherTimer _searchTimer = new();

        public ManagePage()
        {
            InitializeComponent();
            Loaded += ManagePage_Loaded;
            _searchTimer.Interval = TimeSpan.FromMilliseconds(200);
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyFilterAndPage(); };
        }

        private void ManagePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadGames();
        }

        private void LoadGames()
        {
            _allGames.Clear();
            var seenIds = new HashSet<string>();
            int id = 0;

            try
            {
                var steamPath = SteamService.FindSteamPath();
                var libFolders = SteamService.GetSteamLibraryFolders();
                var depotCacheDirs = new List<string>();

                // Collect all steamapps dirs + depotcache dir
                if (steamPath != null)
                {
                    depotCacheDirs.Add(Path.Combine(steamPath, "config", "depotcache"));
                }

                // Scan ALL library folders for appmanifest files
                foreach (var lib in libFolders)
                {
                    if (!Directory.Exists(lib)) continue;
                    foreach (var acf in Directory.GetFiles(lib, "appmanifest_*.acf"))
                    {
                        try
                        {
                            var content = File.ReadAllText(acf);
                            var appIdMatch = Regex.Match(content, @"""appid""\s+""(\d+)""");
                            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
                            var sizeMatch = Regex.Match(content, @"""SizeOnDisk""\s+""(\d+)""");
                            var installDirMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");

                            var appId = appIdMatch.Success ? appIdMatch.Groups[1].Value : "";
                            var name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown";
                            var size = sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var s) ? s : 0;
                            var installDir = installDirMatch.Success ? installDirMatch.Groups[1].Value : "";

                            if (!string.IsNullOrEmpty(appId) && seenIds.Add(appId))
                            {
                                var exePath = FindGameExe(appId, installDir, libFolders);
                                _allGames.Add(new ManageGame
                                {
                                    Id = id++,
                                    Title = name,
                                    AppId = appId,
                                    SizeOnDisk = size,
                                    SizeDisplay = FormatSize(size),
                                    CoverUrl = SteamService.GameImageUrl(appId),
                                    FilePath = acf,
                                    InstallDir = installDir,
                                    ExePath = exePath
                                });
                            }
                        }
                        catch { }
                    }
                }

                // Also scan depotcache for .manifest files (games added via app but maybe no acf)
                foreach (var depotDir in depotCacheDirs.Where(Directory.Exists))
                {
                    foreach (var mf in Directory.GetFiles(depotDir, "*.manifest"))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(mf);
                        // manifest files are named like "depotId_manifestId.manifest" - extract appId from the depotId if possible
                        // Just track that a game has manifests here
                    }
                }
            }
            catch { }

            ApplyFilterAndPage();
        }

        public static string FindGameExe(string appId, string installDir, List<string> libFolders)
        {
            foreach (var lib in libFolders)
            {
                var common = Path.Combine(lib, "common", installDir);
                if (!Directory.Exists(common)) continue;

                var exes = Directory.GetFiles(common, "*.exe", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        return !name.Contains("unins") && !name.Contains("setup") &&
                               !name.Contains("redist") && !name.Contains("_commonredist") &&
                               !name.Contains("dxwebsetup") && !name.Contains("vc_redist");
                    })
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();

                if (exes.Count > 0) return exes[0];

                // Fallback: try any exe named after the install dir
                var dirExe = Path.Combine(common, $"{installDir}.exe");
                if (File.Exists(dirExe)) return dirExe;
            }
            return "";
        }

        private void ApplyFilterAndPage()
        {
            var filtered = string.IsNullOrEmpty(_query)
                ? _allGames
                : _allGames.Where(g =>
                    g.Title.ToLowerInvariant().Contains(_query) ||
                    g.AppId.Contains(_query)).ToList();

            var hasRealGames = _allGames.Any(g => !string.IsNullOrEmpty(g.AppId));
            EmptyState.Visibility = hasRealGames && filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            GameCount.Text = $"{filtered.Count} game(s)";

            var page = filtered.Skip((_page - 1) * PER_PAGE).Take(PER_PAGE).ToList();
            GamesList.ItemsSource = page;

            var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PER_PAGE));
            PaginationPanel.Children.Clear();

            for (int n = 1; n <= totalPages; n++)
            {
                var styleKey = n == _page ? "AccentButton" : "SecondaryButton";
                var style = FindResource(styleKey) as Style;
                
                var btn = new Button
                {
                    Content = n.ToString(),
                    Style = style,
                    Width = 36,
                    Height = 36,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2, 0, 2, 0),
                    FontSize = 13,
                    FontWeight = n == _page ? FontWeights.Bold : FontWeights.Normal
                };
                
                // Fallback styling if style not found
                if (style == null)
                {
                    btn.Background = n == _page 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 46));
                    btn.Foreground = System.Windows.Media.Brushes.White;
                    btn.BorderThickness = new Thickness(1);
                    btn.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 74));
                }
                
                var pageNum = n;
                btn.Click += (_, _) => { _page = pageNum; ApplyFilterAndPage(); };
                PaginationPanel.Children.Add(btn);
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes; int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:F1} {units[i]}";
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.F)
            {
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SearchBox.Text = "Search installed games...";
                _query = "";
                _page = 1;
                ApplyFilterAndPage();
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search installed games...")
                SearchBox.Text = "";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (GamesList == null) return;
            _query = SearchBox.Text.Trim().ToLowerInvariant();
            if (_query == "search installed games...")
                _query = "";
            _page = 1;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _page = 1;
            _query = "";
            SearchBox.Text = "Search installed games...";
            LoadGames();
        }

        private void ContextPlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ManageGame game)
            {
                PlayGame(game);
            }
        }

        private void ContextOpenSteam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
            {
                try { Process.Start(new ProcessStartInfo { FileName = $"https://store.steampowered.com/app/{appId}", UseShellExecute = true }); }
                catch { }
            }
        }

        private void ContextOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string installDir && !string.IsNullOrEmpty(installDir))
            {
                foreach (var lib in SteamService.GetSteamLibraryFolders())
                {
                    var path = Path.Combine(lib, "common", installDir);
                    if (Directory.Exists(path))
                    {
                        try { Process.Start("explorer.exe", path); return; }
                        catch { }
                    }
                }
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

        private void ContextRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ManageGame game)
            {
                Remove_Click(new Button { Tag = game }, null!);
            }
        }

        private void PlayGame(ManageGame game)
        {
            if (!string.IsNullOrEmpty(game.ExePath) && File.Exists(game.ExePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = game.ExePath,
                        WorkingDirectory = Path.GetDirectoryName(game.ExePath),
                        UseShellExecute = true
                    });
                    ToastService.Show($"▶️ Launched {game.Title}", "success");
                    return;
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(game.AppId))
            {
                try
                {
                    var steamUri = $"steam://rungameid/{game.AppId}";
                    Process.Start(new ProcessStartInfo { FileName = steamUri, UseShellExecute = true });
                    ToastService.Show($"▶️ Launching {game.Title} via Steam", "info");
                    return;
                }
                catch { }
            }

            ToastService.Show($"❌ Could not launch {game.Title}", "error");
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ManageGame game)
                PlayGame(game);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ManageGame game)
            {
                var result = MessageBox.Show($"Remove \"{game.Title}\" (App {game.AppId})?",
                    "Remove Game", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                try
                {
                    _manifest.UninstallGameManifest(game.AppId);
                    _allGames.RemoveAll(g => g.Id == game.Id);
                    ApplyFilterAndPage();
                    ToastService.Show($"🗑️ Removed {game.Title}", "success");
                }
                catch (Exception ex)
                {
                    ToastService.Show($"❌ Error: {ex.Message}", "error");
                }
            }
        }
    }
}

