using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpokysProjectLightning.Models;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
{
    public partial class HomePage : UserControl
    {
        private List<ManifestInfo> _games = new();
        private readonly Random _rng = new();
        private readonly SteamService _steam = new();
        private readonly RyuuFixesService _fixes = new();
        private readonly ManifestService _manifest = new();

        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _games = await _steam.GetRecommendedPaidGamesAsync();
            }
            catch
            {
                _games = SteamService.GetFallbackGames();
            }

            if (_games.Count == 0) return;

            var featured = _games[0];
            FeaturedTitle.Text = featured.Name;
            var downloads = 150000 + _rng.Next(850000);
            FeaturedDownloads.Text = $"{downloads:N0} downloads";

            var heroUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{featured.AppId}/header.jpg";
            try
            {
                FeaturedImage.Source = new BitmapImage(new Uri(heroUrl));
            }
            catch
            {
                FeaturedImage.Source = null;
            }

            var manifest = new ManifestService();
            if (manifest.IsGameInstalled(featured.AppId))
            {
                FeaturedPlayBtn.Visibility = Visibility.Visible;
                FeaturedTag.Text = "INSTALLED";
            }

            var topDownloads = _games.Skip(1).Take(6).ToList();
            MostDownloadedItems.ItemsSource = topDownloads;
        }

        private void FeaturedPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_games.Count == 0) return;
            var appId = _games[0].AppId;
            var libFolders = SteamService.GetSteamLibraryFolders();
            var steamPath = SteamService.FindSteamPath();
            if (steamPath == null) return;

            var acfPath = System.IO.Path.Combine(steamPath, "steamapps", $"appmanifest_{appId}.acf");
            if (!System.IO.File.Exists(acfPath)) return;

            var content = System.IO.File.ReadAllText(acfPath);
            var installMatch = System.Text.RegularExpressions.Regex.Match(content, @"""installdir""\s+""([^""]+)""");
            var installDir = installMatch.Success ? installMatch.Groups[1].Value : "";
            if (string.IsNullOrEmpty(installDir)) return;

            var exe = ManagePage.FindGameExe(appId, installDir, libFolders);
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(exe),
                        UseShellExecute = true
                    });
                    ToastService.Show($"▶️ Launching {_games[0].Name}...", "info");
                }
                catch (Exception ex)
                {
                    ToastService.Show($"❌ {ex.Message}", "error");
                }
            }
        }

        private async void ContextInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
                await InstallGameByAppId(appId);
        }

        private void ContextOpenSteam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
                OpenUrl($"https://store.steampowered.com/app/{appId}");
        }

        private void ContextCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
            {
                try { Clipboard.SetText(appId); ToastService.Show($"📋 App ID {appId} copied", "success"); }
                catch { }
            }
        }

        private async Task InstallGameByAppId(string appId)
        {
            var gameName = _games.FirstOrDefault(g => g.AppId == appId)?.Name ?? appId;
            try
            {
                var existing = _manifest.IsGameInstalled(appId);
                if (existing)
                {
                    var msg = MessageBox.Show($"{gameName} is already installed.\n\nRe-install manifests?",
                        "Already Installed", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (msg != MessageBoxResult.Yes) return;
                }

                ToastService.Show($"📥 Queued {gameName} for installation", "info");
                var result = await InstallationManager.Instance.EnqueueAsync(appId, gameName);
                if (result.Success)
                    ToastService.Show($"✅ {gameName} - {result.ManifestsInstalled} manifests installed!", "success", 5000);
                else
                    ToastService.Show($"❌ {gameName}: {result.Message}", "error", 6000);
            }
            catch (Exception ex)
            {
                ToastService.Show($"❌ Error: {ex.Message}", "error", 6000);
            }
        }

        private async void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string appId && !string.IsNullOrEmpty(appId))
            {
                var gameName = _games.FirstOrDefault(g => g.AppId == appId)?.Name ?? appId;
                var existing = _manifest.IsGameInstalled(appId);
                if (existing)
                {
                    var msg = MessageBox.Show(
                        $"{gameName} is already installed.\n\nRe-install manifests?",
                        "Already Installed", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (msg != MessageBoxResult.Yes) return;
                }

                ToastService.Show($"📥 Queued {gameName} for installation", "info");
                var result = await InstallationManager.Instance.EnqueueAsync(appId, gameName);
                if (result.Success)
                    ToastService.Show($"✅ {gameName} - {result.ManifestsInstalled} manifests installed!", "success", 5000);
                else
                    ToastService.Show($"❌ {gameName}: {result.Message}", "error", 6000);
            }
        }

        private void QuickGuide_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://online-fix.me/");
        }

        private void Changelog_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://online-fix.me/");
        }

        private void Legal_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Project Spoky is a community tool for managing game fixes and modifications. "
                          + "All trademarks and copyrights belong to their respective owners.",
                          "Legal Notice", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Project Spoky v4.2.0.0\n\nA community tool for managing Steam game fixes and modifications.",
                          "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        // ===== Drag & Drop archive install =====

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = (Brush)FindResource("AccentBrush");
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0xDE, 0x80));
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0xDE, 0x80));
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            foreach (var file in (string[])e.Data.GetData(DataFormats.FileDrop))
            {
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".rar" or ".zip" or ".7z" or ".001")
                    await InstallDroppedArchive(file);
            }
        }

        private async Task InstallDroppedArchive(string file)
        {
            try
            {
                var installer = new FixInstallerService();
                var result = await installer.InstallLocalArchiveAsync(file, System.IO.Path.GetFileNameWithoutExtension(file));
                MessageBox.Show(result.Message, result.Success ? "Fix Installed" : "Install Failed",
                    MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing {System.IO.Path.GetFileName(file)}:\n{ex.Message}",
                    "Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

