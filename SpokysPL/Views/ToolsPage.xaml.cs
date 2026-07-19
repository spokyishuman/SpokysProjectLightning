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
using Microsoft.Web.WebView2.Wpf;
using SpokysProjectLightning.Services;

namespace SpokysProjectLightning.Views
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
        private bool _webView2Initialized;
        private static readonly string DownloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "ToolDownloads");

        public ToolsPage()
        {
            InitializeComponent();
            Loaded += ToolsPage_Loaded;
        }

        private void ToolsPage_Loaded(object sender, RoutedEventArgs e)
        {
            ToolsList.ItemsSource = new List<ToolInfo>
            {
                new ToolInfo
                {
                    Icon = "📦",
                    Name = "DepotBox",
                    Description = "A Steam depot generator with 133K+ games. Generate and download depot manifests and Lua scripts.",
                    Url = "https://depotbox.org"
                },
                new ToolInfo
                {
                    Icon = "📜",
                    Name = "Ryuu's Manifest",
                    Description = "Generate and download Steam manifests from Ryuu's repository.",
                    Url = "https://generator.ryuu.lol"
                },
                new ToolInfo
                {
                    Icon = "⚡",
                    Name = "LuaTools",
                    Description = "Manifest generator and Steam plugin for managing DLC unlocks and game fixes.",
                    Url = "https://lua.tools"
                },
                new ToolInfo
                {
                    Icon = "🌐",
                    Name = "SteamDB",
                    Description = "Comprehensive Steam database with depots, manifests, and app info.",
                    Url = "https://steamdb.info"
                }
            };
        }

        private async void ToolCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrEmpty(url))
            {
                await NavigateToUrl(url);
            }
        }

        private async Task NavigateToUrl(string url)
        {
            UrlBar.Text = url;
            Placeholder.Visibility = Visibility.Collapsed;
            ToolBrowser.Visibility = Visibility.Visible;

            if (!_webView2Initialized)
            {
                await ToolBrowser.EnsureCoreWebView2Async();
                ToolBrowser.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
                ToolBrowser.CoreWebView2.NewWindowRequested += (s, args) =>
                {
                    args.Handled = true;
                    _ = NavigateToUrl(args.Uri);
                };
                ToolBrowser.CoreWebView2.DownloadStarting += OnDownloadStarting;
                _webView2Initialized = true;
            }

            ToolBrowser.CoreWebView2.Navigate(url);
        }

        private void GoBack_Click(object sender, RoutedEventArgs e)
        {
            if (ToolBrowser.CoreWebView2?.CanGoBack == true)
                ToolBrowser.CoreWebView2.GoBack();
        }

        private void GoForward_Click(object sender, RoutedEventArgs e)
        {
            if (ToolBrowser.CoreWebView2?.CanGoForward == true)
                ToolBrowser.CoreWebView2.GoForward();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ToolBrowser.CoreWebView2?.Reload();
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBar.Text;
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void OnDownloadStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
        {
            var fileName = e.DownloadOperation.ResultFilePath;
            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            // Only intercept manifest-related downloads
            if (ext != ".zip" && ext != ".manifest" && ext != ".lua") return;

            e.Handled = true;

            var destDir = DownloadDir;
            Directory.CreateDirectory(destDir);
            var localPath = Path.Combine(destDir, Path.GetFileName(fileName));
            e.ResultFilePath = localPath;

            e.DownloadOperation.StateChanged += async (_, _) =>
            {
                try
                {
                    if (e.DownloadOperation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted)
                    {
                        ToastService.Show($"❌ Download failed: {Path.GetFileName(localPath)}", "error");
                    }
                    else if (e.DownloadOperation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed)
                    {
                        await InstallDownloadedFile(localPath);
                    }
                }
                catch { }
            };
        }

        private async Task InstallDownloadedFile(string path)
        {
            try
            {
                var steamPath = SteamService.FindSteamPath();
                if (steamPath == null)
                {
                    ToastService.Show("⚠️ File saved but Steam not found. Install manually.", "warning");
                    return;
                }

                var ext = Path.GetExtension(path).ToLowerInvariant();
                var fileName = Path.GetFileName(path);

                if (ext == ".manifest")
                {
                    var depotDir = Path.Combine(steamPath, "config", "depotcache");
                    Directory.CreateDirectory(depotDir);
                    File.Copy(path, Path.Combine(depotDir, fileName), true);
                    ToastService.Show($"✅ Manifest installed: {fileName}", "success");
                }
                else if (ext == ".lua")
                {
                    var luaDir = Path.Combine(steamPath, "config", "stplug-in");
                    Directory.CreateDirectory(luaDir);
                    File.Copy(path, Path.Combine(luaDir, fileName), true);
                    ToastService.Show($"✅ Lua script installed: {fileName}", "success");
                }
                else if (ext == ".zip")
                {
                    int installed = 0;
                    string[] manifestExts = { ".manifest", ".lua", ".vdf" };
                    using var zip = ZipFile.OpenRead(path);
                    foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        var entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (!manifestExts.Contains(entryExt)) continue;

                        string targetDir = entryExt switch
                        {
                            ".lua" => Path.Combine(steamPath, "config", "stplug-in"),
                            _ => Path.Combine(steamPath, "config", "depotcache")
                        };
                        Directory.CreateDirectory(targetDir);
                        entry.ExtractToFile(Path.Combine(targetDir, entry.Name), true);
                        installed++;
                    }
                    if (installed > 0)
                        ToastService.Show($"✅ {installed} file(s) extracted from {fileName}", "success");
                    else
                        ToastService.Show($"⚠️ No manifest files found in {fileName}", "warning");
                }
            }
            catch (Exception ex)
            {
                ToastService.Show($"❌ Failed to install {Path.GetFileName(path)}: {ex.Message}", "error");
            }
        }
    }
}

