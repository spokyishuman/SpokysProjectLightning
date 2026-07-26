using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public class FixGame
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public List<string> Badges { get; set; } = new();
        public string CoverUrl { get; set; } = string.Empty;
        public string FixHref { get; set; } = string.Empty;
        public string FixSize { get; set; } = string.Empty;
        public bool HasFix { get; set; }
        public string BadgesDisplay => Badges.Count > 0 ? string.Join(" · ", Badges) : Tag;
        public List<string> SecondaryBadges => Badges.Count > 1 ? Badges.Skip(1).ToList() : new();
    }

    public partial class FixesPage : UserControl
    {
        private const int PER_PAGE = 24;

        private readonly ManifestService _manifestService = new();
        private readonly RyuuFixesService _ryuuFixes = new();
        private readonly SteamToolsGamesService _steamTools = new();
        private readonly SteamToolsSiteService _steamToolsSite = new();
        private readonly FaresService _faresService = new();

        private readonly List<FixGame> _allGames = new();
        private string _query = string.Empty;
        private int _currentPage = 1;
        private readonly DispatcherTimer _searchTimer = new();
        private bool UseLumaCoreMode => AppMode.UseLumaCore;

        private static readonly string WvUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "WebView2");

        private string? _discordUser;
        private string? _discordAvatarUrl;
        private System.Net.CookieContainer? _sessionCookies;
        private bool _hasSavedLogin;
        private bool IsLoggedIn => _hasSavedLogin;
        private static readonly string CookieStateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "cookies.json");

        private void UpdateLoginUI()
        {
            if (IsLoggedIn)
            {
                DiscordLoginBtn.Visibility = Visibility.Collapsed;
                SignedInPanel.Visibility = Visibility.Visible;
                SignedInText.Text = $"{_discordUser} — click to logout";
                if (!string.IsNullOrEmpty(_discordAvatarUrl))
                    SignedInIcon.Text = "";
            }
            else
            {
                DiscordLoginBtn.Visibility = Visibility.Visible;
                SignedInPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void DiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            DiscordLoginBtn.IsEnabled = false;
            DiscordLoginBtn.Content = "Loading...";

            System.Net.CookieContainer? capturedCookies = null;
            var oauthStarted = false;
            var backOnGenerator = false;

            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, WvUserDataFolder);
                var wv = new WebView2();
                var win = new Window
                {
                    Title = "Discord Login — waiting for generator.ryuu.lol...",
                    Width = 860,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this)
                };
                win.Content = wv;

                // Timer to detect login by checking for session cookies
                DispatcherTimer? checkTimer = null;
                checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                checkTimer.Tick += async (_, _) =>
                {
                    try
                    {
                        if (wv.CoreWebView2 == null || !win.IsVisible) return;
                        // Only accept cookies after OAuth flow was completed
                        if (!oauthStarted || !backOnGenerator) return;

                        var cookies = await wv.CoreWebView2.CookieManager.GetCookiesAsync("https://generator.ryuu.lol");
                        if (cookies.Count > 0)
                        {
                            capturedCookies = new System.Net.CookieContainer();
                            foreach (var c in cookies)
                                capturedCookies.Add(new Uri("https://generator.ryuu.lol"),
                                    new System.Net.Cookie(c.Name, c.Value, "/", "generator.ryuu.lol"));
                            checkTimer?.Stop();
                            ToastService.Show("Logged in with Discord!", "success");
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (win.IsVisible)
                                    win.Close();
                            });
                        }
                    }
                    catch { }
                };

                win.Loaded += async (_, _) =>
                {
                    await wv.EnsureCoreWebView2Async(env);
                    wv.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

                    // Don't handle NewWindowRequested — if the site's popup is blocked,
                    // it falls back to same-window navigation automatically

                    wv.CoreWebView2.NavigationStarting += (_, args) =>
                    {
                        if (args.Uri.StartsWith("discord://", StringComparison.OrdinalIgnoreCase))
                        {
                            oauthStarted = true;
                            backOnGenerator = false;
                            args.Cancel = true;
                            try { Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true }); }
                            catch { }
                            Dispatcher.BeginInvoke(() =>
                            {
                                wv.CoreWebView2?.Navigate("https://generator.ryuu.lol");
                                win.Title = "Authorizing in Discord app — waiting...";
                            });
                        }
                        // Detect any navigation away from generator.ryuu.lol as OAuth start
                        else if (oauthStarted == false && !args.Uri.Contains("generator.ryuu.lol"))
                        {
                            oauthStarted = true;
                            backOnGenerator = false;
                        }
                    };

                    wv.CoreWebView2.SourceChanged += (_, _) =>
                    {
                        try
                        {
                            var src = wv.CoreWebView2?.Source;
                            if (src != null)
                            {
                                var uri = new Uri(src);
                                if (uri.Host.Contains("generator.ryuu.lol"))
                                {
                                    if (oauthStarted)
                                        backOnGenerator = true;
                                    win.Title = "Discord Login — click 'Login with Discord' on the page";
                                }
                                else if (uri.Host.Contains("discord.com"))
                                {
                                    oauthStarted = true;
                                    backOnGenerator = false;
                                    win.Title = "Discord Authorization — log in & click Authorize";
                                }
                            }
                        }
                        catch { }
                    };

                    wv.CoreWebView2.Navigate("https://generator.ryuu.lol");
                    checkTimer?.Start();
                };

                win.Closed += (_, _) =>
                {
                    checkTimer?.Stop();
                    if (capturedCookies == null || capturedCookies.Count == 0)
                    {
                        try
                        {
                            if (wv.CoreWebView2 != null)
                            {
                                var fetch = wv.CoreWebView2.CookieManager.GetCookiesAsync("https://generator.ryuu.lol")
                                    .GetAwaiter().GetResult();
                                if (fetch.Count > 0)
                                {
                                    capturedCookies = new System.Net.CookieContainer();
                                    foreach (var c in fetch)
                                        capturedCookies.Add(new Uri("https://generator.ryuu.lol"),
                                            new System.Net.Cookie(c.Name, c.Value, "/", "generator.ryuu.lol"));
                                }
                            }
                        }
                        catch { }
                    }
                };

                win.ShowDialog();

                if (capturedCookies != null && capturedCookies.Count > 0)
                {
                    _sessionCookies = capturedCookies;
                    _hasSavedLogin = true;
                    _discordUser = "Discord User";
                    SaveLoginState();
                    UpdateLoginUI();
                    ToastService.Show("Logged in with Discord!", "success");
                }
                else
                {
                    MessageBox.Show("Login failed: No session cookie detected.\n\nMake sure you complete the Discord OAuth in the browser window, then close it.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open login page:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DiscordLoginBtn.Content = "Login with Discord";
                DiscordLoginBtn.IsEnabled = true;
            }
        }

        public FixesPage()
        {
            InitializeComponent();
            Loaded += FixesPage_Loaded;
            _searchTimer.Interval = TimeSpan.FromMilliseconds(200);
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyFilter(); };
            PreviewKeyDown += Page_PreviewKeyDown;
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
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
                SearchBox.Clear();
                _query = "";
                _currentPage = 1;
                ApplyFilter();
            }
        }

        private static readonly string LoginStateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "discord_login.json");

        private async void FixesPage_Loaded(object sender, RoutedEventArgs e)
        {
            ManifestPaths.EnsureDirs();
            LoadSavedLogin();
            UpdateSteamDaddyBadge();
            await LoadGamesAsync();
        }

        private void UpdateSteamDaddyBadge()
        {
            var settings = new DataService().LoadSettings();
            if (!string.IsNullOrEmpty(settings.SteamDaddyApiKey))
            {
                SteamDaddyBadge.Visibility = Visibility.Visible;
                SteamDaddyStatusText.Text = $"SD: key set ({settings.SteamDaddyApiKey.Substring(0, Math.Min(8, settings.SteamDaddyApiKey.Length))}...)";
            }
            else
            {
                SteamDaddyBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadSavedLogin()
        {
            try
            {
                if (File.Exists(LoginStateFile))
                {
                    var json = File.ReadAllText(LoginStateFile);
                    var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (data != null && data.TryGetValue("username", out var name) && !string.IsNullOrEmpty(name))
                    {
                        _discordUser = name;
                        _hasSavedLogin = true;
                    }
                }
            }
            catch { }

            // Restore cookies from disk
            try
            {
                if (File.Exists(CookieStateFile))
                {
                    var json = File.ReadAllText(CookieStateFile);
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                    if (list != null && list.Count > 0)
                    {
                        var cc = new System.Net.CookieContainer();
                        foreach (var entry in list)
                        {
                            if (entry.TryGetValue("n", out var n) && entry.TryGetValue("v", out var v))
                            {
                                var p = entry.GetValueOrDefault("p", "/");
                                var d = entry.GetValueOrDefault("d", "generator.ryuu.lol");
                                cc.Add(new Uri("https://generator.ryuu.lol"),
                                    new System.Net.Cookie(n, v, p, d));
                            }
                        }
                        _sessionCookies = cc;
                    }
                }
            }
            catch { }

            UpdateLoginUI();
        }

        private void Logout()
        {
            _sessionCookies = null;
            _discordUser = null;
            _discordAvatarUrl = null;
            _hasSavedLogin = false;
            try { if (File.Exists(LoginStateFile)) File.Delete(LoginStateFile); } catch { }
            try { if (File.Exists(CookieStateFile)) File.Delete(CookieStateFile); } catch { }
            try { if (Directory.Exists(WvUserDataFolder)) Directory.Delete(WvUserDataFolder, true); } catch { }
            UpdateLoginUI();
        }

        private void SignedInPanel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Log out of Discord?", "Logout",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                Logout();
        }

        private void SaveLoginState()
        {
            try
            {
                var dir = Path.GetDirectoryName(LoginStateFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var data = new Dictionary<string, string> { { "username", _discordUser ?? "" } };
                File.WriteAllText(LoginStateFile, System.Text.Json.JsonSerializer.Serialize(data));
            }
            catch { }

            // Persist cookies to disk
            if (_sessionCookies != null)
            {
                try
                {
                    var cookieList = new List<Dictionary<string, string>>();
                    foreach (System.Net.Cookie c in _sessionCookies.GetCookies(new Uri("https://generator.ryuu.lol")))
                    {
                        cookieList.Add(new Dictionary<string, string>
                        {
                            { "n", c.Name },
                            { "v", c.Value },
                            { "p", c.Path ?? "/" },
                            { "d", c.Domain ?? "generator.ryuu.lol" }
                        });
                    }
                    var dir2 = Path.GetDirectoryName(CookieStateFile);
                    if (!string.IsNullOrEmpty(dir2)) Directory.CreateDirectory(dir2);
                    File.WriteAllText(CookieStateFile, System.Text.Json.JsonSerializer.Serialize(cookieList));
                }
                catch { }
            }
        }

        private async Task LoadGamesAsync()
        {
            _allGames.Clear();
            int id = 1;

            try
            {
                var fixes = await _ryuuFixes.GetAllFixesAsync();
                var seen = new HashSet<string>();
                foreach (var f in fixes)
                {
                    if (seen.Add(f.AppId))
                    {
                        _allGames.Add(new FixGame
                        {
                            Id = $"rf{id++}",
                            Title = f.Name,
                            AppId = f.AppId,
                            Tag = f.Badges.Count > 0 ? f.Badges[0] : "FIX",
                            Badges = f.Badges,
                            CoverUrl = SteamService.GameImageUrl(f.AppId),
                            HasFix = true,
                            FixHref = f.Href,
                            FixSize = f.Size
                        });
                    }
                }
            }
            catch { }

            try
            {
                var fallback = SteamService.GetFallbackGames();
                foreach (var f in fallback)
                {
                    if (_allGames.Any(g => g.AppId == f.AppId)) continue;
                    _allGames.Add(new FixGame
                    {
                        Id = $"fb{id++}",
                        Title = f.Name,
                        AppId = f.AppId,
                        Tag = "MANIFEST",
                        CoverUrl = f.ImageUrl
                    });
                }
            }
            catch { }

            try
            {
                var dataFix = new DataService().LoadDataFix();
                foreach (var g in dataFix)
                {
                    if (string.IsNullOrEmpty(g.AppId) || !int.TryParse(g.AppId, out _)) continue;
                    if (_allGames.Any(x => x.AppId == g.AppId)) continue;
                    _allGames.Add(new FixGame
                    {
                        Id = $"df{id++}",
                        Title = g.Name,
                        AppId = g.AppId,
                        Tag = "FIX",
                        CoverUrl = SteamService.GameImageUrl(g.AppId)
                    });
                }
            }
            catch { }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = _query.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(q)
                ? _allGames.ToList()
                : _allGames.Where(g =>
                    g.Title.ToLowerInvariant().Contains(q) ||
                    g.AppId.Contains(q)).ToList();

            var pageCount = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / PER_PAGE));
            _currentPage = Math.Min(_currentPage, pageCount);
            var visible = filtered.Skip((_currentPage - 1) * PER_PAGE).Take(PER_PAGE).ToList();

            GamesList.ItemsSource = visible;
            BuildPagination(pageCount);
        }

        private void BuildPagination(int pageCount)
        {
            PaginationPanel.Children.Clear();

            const int maxVisible = 7;
            int start = Math.Max(1, _currentPage - maxVisible / 2);
            int end = Math.Min(pageCount, start + maxVisible - 1);
            if (end - start + 1 < maxVisible)
                start = Math.Max(1, end - maxVisible + 1);

            var prevBtn = new Button
            {
                Content = "<",
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 4, 0),
                IsEnabled = _currentPage > 1
            };
            prevBtn.Click += PrevPage_Click;
            PaginationPanel.Children.Add(prevBtn);

            if (start > 1)
            {
                var first = new Button { Content = "1", Width = 36, Height = 36, Margin = new Thickness(0, 0, 4, 0), Tag = 1 };
                first.Click += (_, _) => GoToPage(1);
                PaginationPanel.Children.Add(first);
                if (start > 2)
                    PaginationPanel.Children.Add(new TextBlock { Text = "...", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = TryFindResource("MutedForegroundBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray });
            }

            for (int i = start; i <= end; i++)
            {
                var page = i;
                var btn = new Button
                {
                    Content = page.ToString(),
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(0, 0, 4, 0),
                    Tag = page
                };
                btn.Style = page == _currentPage
                    ? TryFindResource("AccentButton") as Style
                    : TryFindResource("SecondaryButton") as Style;
                btn.Click += (_, _) => GoToPage(page);
                PaginationPanel.Children.Add(btn);
            }

            if (end < pageCount)
            {
                if (end < pageCount - 1)
                    PaginationPanel.Children.Add(new TextBlock { Text = "...", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = TryFindResource("MutedForegroundBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray });
                var last = new Button { Content = pageCount.ToString(), Width = 36, Height = 36, Margin = new Thickness(0, 0, 4, 0), Tag = pageCount };
                last.Click += (_, _) => GoToPage(pageCount);
                PaginationPanel.Children.Add(last);
            }

            var nextBtn = new Button
            {
                Content = ">",
                Width = 36,
                Height = 36,
                IsEnabled = _currentPage < pageCount
            };
            nextBtn.Click += NextPage_Click;
            PaginationPanel.Children.Add(nextBtn);
        }

        private void GoToPage(int page)
        {
            _currentPage = page;
            ApplyFilter();
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; ApplyFilter(); }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++; ApplyFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _query = SearchBox.Text;
            _currentPage = 1;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void ContextManifest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is FixGame game)
                InstallManifest_Click(new Button { Tag = game, Content = "Manifest" }, null!);
        }

        private void ContextFix_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is FixGame game)
                ApplyFix_Click(new Button { Tag = game, Content = "Fix" }, null!);
        }

        private void ContextOpenSteam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
                try { Process.Start(new ProcessStartInfo { FileName = $"https://store.steampowered.com/app/{appId}", UseShellExecute = true }); } catch { }
        }

        private void ContextCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string appId)
                try { Clipboard.SetText(appId); ToastService.Show($"App ID {appId} copied", "success"); } catch { }
        }

        private static (string luaDir, string manifestDir, string keysDir) TargetDirs => (
            ManifestPaths.LuaDir,
            ManifestPaths.ManifestDir,
            ManifestPaths.KeysDir
        );

        private async void InstallLumaCore_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            btn.IsEnabled = false;
            var original = btn.Content;
            btn.Content = "Downloading LumaCore...";
            try
            {
                var steamPath = SteamService.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    MessageBox.Show("Steam installation not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var steamProcs = Process.GetProcessesByName("steam");
                if (steamProcs.Length > 0)
                {
                    var result = MessageBox.Show("Steam is currently running. Close Steam now?", "LumaCore Install",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes) { foreach (var p in steamProcs) p.Kill(); await Task.Delay(1000); }
                    else if (result == MessageBoxResult.Cancel) return;
                }
                btn.Content = "Installing...";
                var lc = new LumaCoreService();
                int extracted = await lc.InstallAsync();
                var msg = $"LumaCore installed successfully!\n\nFiles placed in: {steamPath}\nDLLs installed: {extracted}\n\nPlease restart Steam for changes to take effect.";
                if (steamProcs.Length > 0) msg += "\n\nSteam was closed during installation — you can now restart it.";
                MessageBox.Show(msg, "LumaCore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Permission denied. Try running the app as Administrator.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to install LumaCore:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.Content = original;
                btn.IsEnabled = true;
            }
        }

        private async void RestartSteam_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                foreach (var p in Process.GetProcessesByName("steam")) p.Kill();
                await Task.Delay(2000);
                var steamPath = SteamService.FindSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var exe = Path.Combine(steamPath, "steam.exe");
                    if (File.Exists(exe)) Process.Start(exe);
                }
            }
            catch { }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        private async void InstallManifest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FixGame game)
            {
                btn.IsEnabled = false;
                btn.Content = "Manifest...";
                var errors = new List<string>();
                try
                {
                    var steamPath = SteamService.FindSteamPath();
                    if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                    {
                        MessageBox.Show("Steam not found. Install Steam first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    var (luaDir, manifestDir, keysDir) = TargetDirs;
                    ManifestPaths.EnsureDirs();
                    bool installed = false;
                    var settings = new DataService().LoadSettings();
                    var sdKey = settings.SteamDaddyApiKey;
                    var steamDaddy = !string.IsNullOrEmpty(sdKey) ? new SteamDaddyService(sdKey) : null;

                    async Task InstallZipBytes(byte[] zipBytes)
                    {
                        using var ms = new MemoryStream(zipBytes);
                        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            if (entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                                entry.ExtractToFile(Path.Combine(manifestDir, entry.Name), true);
                            else if (entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                                entry.ExtractToFile(Path.Combine(luaDir, entry.Name), true);
                            else if (entry.Name.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase))
                                entry.ExtractToFile(Path.Combine(keysDir, entry.Name), true);
                        }
                    }

                    async Task InstallFaresManifest(string appId)
                    {
                        var faresResult = await _faresService.GetManifestsAsync(appId);
                        if (faresResult == null || faresResult.Manifests.Count == 0)
                        { errors.Add("fares.top: no manifest data"); return; }
                        int downloaded = 0;
                        foreach (var m in faresResult.Manifests)
                        {
                            try
                            {
                                var bytes = await _faresService.DownloadManifestAsync(appId, m.DepotId, m.ManifestId);
                                if (bytes == null || bytes.Length == 0) continue;
                                await File.WriteAllBytesAsync(Path.Combine(manifestDir, $"{m.DepotId}_{m.ManifestId}.manifest"), bytes);
                                downloaded++;
                            }
                            catch { }
                        }
                        if (downloaded == 0)
                        { errors.Add("fares.top: downloaded 0 manifests"); return; }
                        var lua = _manifestService.GenerateLuaList(appId, faresResult.Name,
                            faresResult.Manifests.Select(m => (m.DepotId, m.ManifestId, m.Size)).ToList());
                        await File.WriteAllTextAsync(Path.Combine(luaDir, $"{appId}.lua"), lua);
                        btn.Content = "Manifest";
                        ToastService.Show($"✅ {faresResult.Name} — Manifest from fares.top!", "success");
                        installed = true;
                    }

                    if (UseLumaCoreMode)
                    {
                        if (!installed)
                        {
                            try { btn.Content = "fares.top..."; await Task.Delay(100); await InstallFaresManifest(game.AppId); }
                            catch (Exception ex) { errors.Add($"fares.top: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        try
                        {
                            btn.Content = "steamtools.games...";
                            await Task.Delay(100);
                            var stManifest = await _steamTools.GenerateManifestAsync(game.AppId);
                            if (stManifest != null && !string.IsNullOrEmpty(stManifest.DownloadUrl))
                            {
                                var luaBytes = await _steamTools.DownloadFileAsync(stManifest.LuaUrl);
                                if (luaBytes != null && luaBytes.Length > 0)
                                    await File.WriteAllBytesAsync(Path.Combine(luaDir, $"{game.AppId}.lua"), luaBytes);
                                var keyBytes = await _steamTools.DownloadFileAsync(stManifest.KeyVdfUrl);
                                if (keyBytes != null && keyBytes.Length > 0)
                                    await File.WriteAllBytesAsync(Path.Combine(keysDir, "key.vdf"), keyBytes);
                                var zipBytes = await _steamTools.DownloadFileAsync(stManifest.DownloadUrl);
                                if (zipBytes != null && zipBytes.Length > 0)
                                    await InstallZipBytes(zipBytes);
                                btn.Content = "Manifest";
                                ToastService.Show($"✅ {game.Title} — Manifest from steamtools.games!", "success");
                                installed = true;
                            }
                            else errors.Add("steamtools.games: no manifest data returned");
                        }
                        catch (Exception ex) { errors.Add($"steamtools.games: {ex.Message}"); }
                    }

                    if (!installed)
                    {
                        if (UseLumaCoreMode)
                        {
                            try
                            {
                                btn.Content = "steamtools.games...";
                                await Task.Delay(100);
                                var stManifest = await _steamTools.GenerateManifestAsync(game.AppId);
                                if (stManifest != null && !string.IsNullOrEmpty(stManifest.DownloadUrl))
                                {
                                    var luaBytes = await _steamTools.DownloadFileAsync(stManifest.LuaUrl);
                                    if (luaBytes != null && luaBytes.Length > 0)
                                        await File.WriteAllBytesAsync(Path.Combine(luaDir, $"{game.AppId}.lua"), luaBytes);
                                    var keyBytes = await _steamTools.DownloadFileAsync(stManifest.KeyVdfUrl);
                                    if (keyBytes != null && keyBytes.Length > 0)
                                        await File.WriteAllBytesAsync(Path.Combine(keysDir, "key.vdf"), keyBytes);
                                    var zipBytes = await _steamTools.DownloadFileAsync(stManifest.DownloadUrl);
                                    if (zipBytes != null && zipBytes.Length > 0)
                                        await InstallZipBytes(zipBytes);
                                    btn.Content = "Manifest";
                                    ToastService.Show($"✅ {game.Title} — Manifest from steamtools.games!", "success");
                                    installed = true;
                                }
                                else errors.Add("steamtools.games: no manifest data");
                            }
                            catch (Exception ex) { errors.Add($"steamtools.games: {ex.Message}"); }
                        }
                        else
                        {
                            try { btn.Content = "fares.top..."; await Task.Delay(100); await InstallFaresManifest(game.AppId); }
                            catch (Exception ex) { errors.Add($"fares.top: {ex.Message}"); }
                        }
                    }

                    if (!installed && steamDaddy != null)
                    {
                        try
                        {
                            btn.Content = "SteamDaddy...";
                            await Task.Delay(100);
                            var result = await steamDaddy.FetchManifestAsync(game.AppId);
                            if (result?.IsZip == true && result.ExtractedFiles.Count > 0)
                            {
                                foreach (var f in result.ExtractedFiles)
                                {
                                    var dest = f.IsLua ? Path.Combine(luaDir, f.FileName) : Path.Combine(manifestDir, f.FileName);
                                    await File.WriteAllBytesAsync(dest, f.Data);
                                }
                                btn.Content = "SteamDaddy";
                                ToastService.Show($"✅ {game.Title} — Manifest from SteamDaddy!", "success");
                                installed = true;
                            }
                            else errors.Add("SteamDaddy: no manifest data");
                        }
                        catch (Exception ex) { errors.Add($"SteamDaddy: {ex.Message}"); }
                    }

                    if (!installed)
                    {
                        try
                        {
                            btn.Content = "steamtools.site...";
                            await Task.Delay(100);
                            var zipBytes = await _steamToolsSite.DownloadManifestZipAsync(game.AppId);
                            if (zipBytes != null && zipBytes.Length > 0)
                            {
                                await InstallZipBytes(zipBytes);
                                btn.Content = "Manifest";
                                ToastService.Show($"✅ {game.Title} — Manifest from steamtools.site!", "success");
                                installed = true;
                            }
                            else errors.Add("steamtools.site: empty/no zip");
                        }
                        catch (Exception ex) { errors.Add($"steamtools.site: {ex.Message}"); }
                    }

                    if (!installed)
                    {
                        btn.Content = "Manifest";
                        var detail = string.Join("\n", errors.Select((e, i) => $"  {i + 1}. {e}"));
                        MessageBox.Show(
                            $"❌ All manifest sources failed for {game.Title} (App {game.AppId}):\n\n{detail}\n\n" +
                            "Tips:\n• Check your internet connection\n• Get a SteamDaddy API key from discord.gg/XN6YGcUF89\n• Try the 'Fix' button instead\n• Try using OpenSteamTool (OST page) for auto-manifest",
                            "Manifest Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        try
                        {
                            var depotCache = Path.Combine(steamPath, "depotcache");
                            var configDepot = Path.Combine(steamPath, "config", "depotcache");
                            var stplugIn = Path.Combine(steamPath, "config", "stplug-in");
                            Directory.CreateDirectory(depotCache);
                            Directory.CreateDirectory(configDepot);
                            Directory.CreateDirectory(stplugIn);
                            foreach (var f in Directory.GetFiles(manifestDir, "*.manifest"))
                            {
                                var name = Path.GetFileName(f);
                                File.Copy(f, Path.Combine(depotCache, name), true);
                                File.Copy(f, Path.Combine(configDepot, name), true);
                            }
                            foreach (var f in Directory.GetFiles(luaDir, "*.lua"))
                                File.Copy(f, Path.Combine(stplugIn, Path.GetFileName(f)), true);
                        }
                        catch (Exception ex) { Debug.WriteLine($"[InstallManifest] Copy to Steam dirs failed: {ex.Message}"); }
                        var sdSvc = new SteamDaddyService();
                        var sdExe = sdSvc.GetExePath();
                        if (sdExe != null) try { Process.Start(new ProcessStartInfo { FileName = sdExe, UseShellExecute = true }); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    btn.Content = "Manifest";
                    MessageBox.Show($"❌ Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { btn.IsEnabled = true; }
            }
        }

        private async void ApplyFix_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FixGame game)
            {
                btn.IsEnabled = false;
                btn.Content = "Fixing...";

                try
                {
                    var steamPath = SteamService.FindSteamPath();
                    if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                    {
                        btn.Content = "Fix";
                        MessageBox.Show("Steam not found. Install Steam first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var fix = await _ryuuFixes.GetFixForAppAsync(game.AppId);
                    if (fix == null || string.IsNullOrEmpty(fix.Href))
                    {
                        btn.Content = "Fix";
                        MessageBox.Show($"❌ No fix available for {game.Title} on Ryuu's repository.", "No Fix", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    btn.Content = "Downloading...";
                    byte[]? zipBytes;

                    // Try download with the saved session cookie (HTTP-only auth cookie)
                    if (_sessionCookies != null && _sessionCookies.Count > 0)
                        zipBytes = await DownloadWithSessionCookieAsync(fix.Href);
                    else
                        zipBytes = await _ryuuFixes.DownloadFixAsync(fix.Href);

                    if (zipBytes == null || zipBytes.Length == 0)
                    {
                        btn.Content = "Fix";
                        MessageBox.Show("❌ Could not download fix.\nMake sure you're logged in with Discord.", "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var folderDlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to extract the fix files" };
                    if (folderDlg.ShowDialog(Window.GetWindow(this)) != true)
                    { btn.Content = "Fix"; return; }
                    var extractDir = folderDlg.FolderName;

                    var excl = MessageBox.Show($"Add a Windows Defender exclusion for:\n{extractDir}\n\nThis prevents Defender from removing the fix files. (Admin required)", "Defender Exclusion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (excl == MessageBoxResult.Yes) AddDefenderExclusion(extractDir);

                    btn.Content = "Extracting...";
                    Directory.CreateDirectory(extractDir);
                    using var ms = new MemoryStream(zipBytes);
                    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                    int extracted = 0;
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var destPath = Path.Combine(extractDir, entry.FullName);
                        var parent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        try { entry.ExtractToFile(destPath, true); extracted++; }
                        catch { }
                    }

                    btn.Content = "Fix";
                    MessageBox.Show($"✅ {game.Title} — Fix extracted!\n\nExtracted {extracted} file(s) to:\n{extractDir}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    btn.Content = "Fix";
                    MessageBox.Show($"❌ Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { btn.IsEnabled = true; }
            }
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
            catch
            {
                MessageBox.Show("Could not add Defender exclusion. Try running the app as Administrator.", "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<byte[]?> DownloadWithSessionCookieAsync(string href)
        {
            if (_sessionCookies == null || _sessionCookies.Count == 0) return null;

            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                CookieContainer = _sessionCookies,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            http.DefaultRequestHeaders.Referrer = new Uri("https://generator.ryuu.lol/fixes/");
            http.Timeout = TimeSpan.FromSeconds(60);

            const string BaseUrl = "https://generator.ryuu.lol";
            var fileName = Path.GetFileName(href.TrimEnd('/').Replace("%20", " "));
            var urls = new List<string>();

            if (href.StartsWith("http"))
            {
                urls.Add(href);
                if (!string.IsNullOrEmpty(fileName))
                    urls.Add($"{BaseUrl}/files/{fileName}");
            }
            else
            {
                var clean = href.TrimStart('/');
                urls.Add($"{BaseUrl}/{clean}");
                urls.Add($"{BaseUrl}/files/{clean}");
                if (clean.Contains('/') && !string.IsNullOrEmpty(fileName))
                {
                    urls.Add($"{BaseUrl}/{fileName}");
                    urls.Add($"{BaseUrl}/files/{fileName}");
                }
            }

            urls = urls.Distinct().ToList();

            foreach (var url in urls)
            {
                try
                {
                    var escaped = url.Replace(" ", "%20");
                    using var req = new HttpRequestMessage(HttpMethod.Get, escaped);
                    var resp = await http.SendAsync(req);
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";

                    if (ct.Contains("text/html") || ct.Contains("text/plain"))
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        if (body.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("login", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsByteArrayAsync();
                }
                catch
                {
                    continue;
                }
            }
            return null;
        }

    }
}
