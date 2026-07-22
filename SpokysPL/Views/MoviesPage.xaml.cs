using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using SpokysProjectVercel.Models;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public partial class MoviesPage : UserControl
    {
        private readonly MovieScraperService _scraper;
        private bool _hasLoaded;
        private bool _webViewInitialized;
        private List<MovieResult> _currentMovies = new();
        private MovieResult? _currentPlaying;
        private int _currentSourceIndex;
        private int _currentSeason = 1;
        private int _currentEpisode = 1;
        private bool _isFullscreen;
        private MainWindow? _fullscreenWindow;
        private List<string> _currentUrls = new();
        private readonly Dictionary<string, double> _playbackPositions = new();
        private string _currentPositionKey = "";
        private DispatcherTimer? _positionTimer;
        private DispatcherTimer? _searchTimer;

        private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "googleads.g.doubleclick.net", "adservice.google.com", "pagead2.googlesyndication.com",
            "adnxs.com", "adsrvr.org", "criteo.com", "criteo.net",
            "outbrain.com", "taboola.com", "scorecardresearch.com", "quantserve.com",
            "rubiconproject.com", "openx.net", "pubmatic.com", "indexww.com",
            "demdex.net", "adsafeprotected.com", "moatads.com", "casalemedia.com",
            "media.net", "adroll.com", "sharethrough.com", "adform.net",
            "amazon-adsystem.com", "bidswitch.net", "adzerk.net", "krxd.net",
            "popads.net", "popunder.net", "popcash.net", "adf.ly",
            "shorte.st", "linkbucks.com", "adfoc.us", "propellerads.com",
            "hilltopads.net", "exoclick.com", "juicyads.com", "trafficjunky.net",
            "revcontent.com", "mgid.com", "zergnet.com", "content.ad",
            "google-analytics.com", "googletagmanager.com", "facebook.net",
            "connect.facebook.net", "analytics.twitter.com",
            "cdn.popads.net", "cdn.popjs.net", "popunderjs.com",
            "pushame.com", "pushengage.com", "onesignal.com",
            "bit.ly", "tinyurl.com", "t.co", "is.gd",
            "sh.st", "ouo.io", "bc.vc", "linkshrink.net",
        };

        public MoviesPage()
        {
            InitializeComponent();
            _scraper = new MovieScraperService();
            Loaded += MoviesPage_Loaded;
            IsVisibleChanged += OnIsVisibleChanged;
            PreviewKeyDown += Page_KeyDown;
            UpdateSearchPlaceholder();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchPlaceholder();
            if (_searchTimer == null)
            {
                _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _searchTimer.Tick += (s, args) =>
                {
                    _searchTimer.Stop();
                    _ = PerformSearch();
                };
            }
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private async void MoviesPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_hasLoaded)
            {
                _hasLoaded = true;
                await LoadRecommended();
                // Pre-warm the WebView2 player so the first click plays instantly (no buffering spin)
                _ = EnsurePlayerInitializedAsync();
            }
        }

        private async Task LoadRecommended()
        {
            // Priority: proxy/TMDB API → bundled database → hardcoded fallback
            try
            {
                var fromApi = await _scraper.GetRecommendedAsync();
                if (fromApi.Count > 0)
                {
                    RecommendedGrid.ItemsSource = fromApi.Take(10).ToList();
                    ResultsHeader.Text = _scraper.SourceLabel;
                    _currentMovies = fromApi;
                    MoviesGrid.ItemsSource = _currentMovies;
                    LoadingText.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch { }

            var bundled = _scraper.LoadBundled();
            if (bundled.Count > 0)
            {
                var shuffled = bundled.OrderBy(_ => Random.Shared.Next()).ToList();
                RecommendedGrid.ItemsSource = shuffled.Take(10).ToList();
                ResultsHeader.Text = $"📚 Local Database ({bundled.Count} titles)";
                _currentMovies = shuffled;
                MoviesGrid.ItemsSource = _currentMovies;
                LoadingText.Visibility = Visibility.Collapsed;
                NoResultsHeader.Visibility = Visibility.Collapsed;
                return;
            }

            // Fallback: hardcoded list as last resort
            var fallback = new List<MovieResult>
            {
                new() { Id = 550, Title = "Fight Club", PosterPath = "/pB8BM7pdSp6B6Ih7QZ4DrQ3PmJK.jpg", VoteAverage = 8.4, ReleaseDate = "1999-10-15", MediaType = "movie" },
                new() { Id = 680, Title = "Pulp Fiction", PosterPath = "/d5iIlFn5s0ImszYzBPb8JPIfbXD.jpg", VoteAverage = 8.5, ReleaseDate = "1994-09-10", MediaType = "movie" },
                new() { Id = 155, Title = "The Dark Knight", PosterPath = "/qJ2tW6WMUDux911BytTEeJqa3sT.jpg", VoteAverage = 8.5, ReleaseDate = "2008-07-16", MediaType = "movie" },
                new() { Id = 244786, Title = "Whiplash", PosterPath = "/7fn624j5lj3xTme2SgiLCeuedmO.jpg", VoteAverage = 8.4, ReleaseDate = "2014-10-10", MediaType = "movie" },
                new() { Id = 278, Title = "The Shawshank Redemption", PosterPath = "/9cjIGRQL1m4E87V2h5RrYJ8yXjM.jpg", VoteAverage = 8.7, ReleaseDate = "1994-09-23", MediaType = "movie" },
                new() { Id = 238, Title = "The Godfather", PosterPath = "/3bhkrj58Vtu7enYsRolD1fZdja1.jpg", VoteAverage = 8.7, ReleaseDate = "1972-03-14", MediaType = "movie" },
                new() { Id = 424, Title = "Schindler's List", PosterPath = "/sF1U4EUQS8YHUYjNl3pMGNIQyr0.jpg", VoteAverage = 8.6, ReleaseDate = "1993-11-30", MediaType = "movie" },
                new() { Id = 497, Title = "The Green Mile", PosterPath = "/velWPhVMQeQKcxggNEU8YmIo52R.jpg", VoteAverage = 8.5, ReleaseDate = "1999-12-10", MediaType = "movie" },
                new() { Id = 807, Title = "Se7en", PosterPath = "/6yoghtyTpznpBik8EngEmJskVUO.jpg", VoteAverage = 8.3, ReleaseDate = "1995-09-22", MediaType = "movie" },
                new() { Id = 769, Title = "Goodfellas", PosterPath = "/aKuFiU82s5ISJDx2cPZ1E5W6F8o.jpg", VoteAverage = 8.5, ReleaseDate = "1990-09-12", MediaType = "movie" },
            };

            RecommendedGrid.ItemsSource = fallback.Take(5).ToList();
            ResultsHeader.Text = "🔥 Popular Movies (fallback)";
            _currentMovies = fallback;
            MoviesGrid.ItemsSource = _currentMovies;
            LoadingText.Visibility = Visibility.Collapsed;
            NoResultsHeader.Visibility = Visibility.Collapsed;
            NoMoviesText.Text = "movies.json not found — bundled database missing.";
        }

        private async Task LoadCategory(string category)
        {
            LoadingText.Visibility = Visibility.Visible;
            LoadingText.Text = "Loading movies...";
            MoviesGrid.ItemsSource = null;
            NoResultsHeader.Visibility = Visibility.Collapsed;
            var bundled = _scraper.LoadBundled();

            try
            {
                switch (category)
                {
                    case "Trending":
                        ResultsHeader.Text = "🔥 Trending Now";
                        _currentMovies = await _scraper.GetTrendingAsync();
                        break;
                    case "Popular":
                        ResultsHeader.Text = "⭐ Popular Movies";
                        _currentMovies = await _scraper.GetPopularMoviesAsync();
                        break;
                    case "TopRated":
                        ResultsHeader.Text = "🏆 Top Rated";
                        _currentMovies = await _scraper.GetTopRatedAsync();
                        break;
                    case "NowPlaying":
                        ResultsHeader.Text = "📅 Now Playing";
                        _currentMovies = await _scraper.GetNowPlayingAsync();
                        break;
                    case "Upcoming":
                        ResultsHeader.Text = "🎬 Upcoming";
                        _currentMovies = await _scraper.GetUpcomingAsync();
                        break;
                    case "OnTV":
                        ResultsHeader.Text = "📺 On TV";
                        _currentMovies = await _scraper.GetOnTVAsync();
                        break;
                }
                if (_currentMovies.Count == 0)
                {
                    if (bundled.Count > 0)
                    {
                        _currentMovies = category switch
                        {
                            "OnTV" => bundled.Where(m => m.IsTv).ToList(),
                            _ => bundled.Where(m => !m.IsTv).ToList()
                        };
                        ResultsHeader.Text = $"📚 Local Database ({_currentMovies.Count} titles)";
                    }
                    else
                    {
                        NoResultsHeader.Visibility = Visibility.Visible;
                        NoMoviesText.Text = $"No movies found in this category.";
                    }
                }
                else
                {
                    ResultsHeader.Text = _scraper.SourceLabel;
                }
                MoviesGrid.ItemsSource = _currentMovies;
            }
            catch (Exception ex)
            {
                if (bundled.Count > 0)
                {
                    _currentMovies = category switch
                    {
                        "OnTV" => bundled.Where(m => m.IsTv).ToList(),
                        _ => bundled.Where(m => !m.IsTv).ToList()
                    };
                    ResultsHeader.Text = $"📚 Local Database ({_currentMovies.Count} titles)";
                    MoviesGrid.ItemsSource = _currentMovies;
                }
                else
                {
                    ResultsHeader.Text = $"❌ Error: {ex.Message}";
                    NoResultsHeader.Visibility = Visibility.Visible;
                    NoMoviesText.Text = $"Failed to load: {ex.Message}";
                }
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholder();
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholder();
        }

        private void UpdateSearchPlaceholder()
        {
            if (SearchPlaceholder == null || SearchBox == null)
                return;

            SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchTimer?.Stop();
                _ = PerformSearch();
            }
            else if (e.Key == Key.Escape)
            {
                SearchBox.Text = "";
                UpdateSearchPlaceholder();
                ResultsHeader.Text = "🔥 Trending Now";
                MoviesGrid.ItemsSource = _currentMovies;
                NoResultsHeader.Visibility = Visibility.Collapsed;
            }
        }

        private void Page_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
            => await PerformSearch();

        private async Task PerformSearch()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == "Search movies & TV shows..." || query.Length < 2)
            {
                LoadingText.Visibility = Visibility.Collapsed;
                MoviesGrid.ItemsSource = _currentMovies;
                return;
            }

            try
            {
                LoadingText.Visibility = Visibility.Visible;
                LoadingText.Text = $"🔍 Searching for '{query}'...";
                MoviesGrid.ItemsSource = null;
                NoResultsHeader.Visibility = Visibility.Collapsed;

                System.Diagnostics.Debug.WriteLine($"Performing search for: '{query}'");
                var results = await _scraper.SearchMoviesAsync(query);
                System.Diagnostics.Debug.WriteLine($"Search returned {results.Count} results");
                
                _currentMovies = results;
                ResultsHeader.Text = results.Count > 0
                    ? $"🔍 Results for '{query}' ({results.Count} found)"
                    : $"❌ No results for '{query}'";
                MoviesGrid.ItemsSource = results;
                if (results.Count == 0)
                {
                    NoResultsHeader.Visibility = Visibility.Visible;
                    NoMoviesText.Text = $"No results found for '{query}'. Try a different search term.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
                ResultsHeader.Text = $"❌ Search error";
                NoResultsHeader.Visibility = Visibility.Visible;
                NoMoviesText.Text = $"Search failed: {ex.Message}";
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private async void CategoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string category)
                await LoadCategory(category);
        }

        private async void MovieCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is MovieResult movie)
            {
                // Save position of currently playing before switching
                await SaveCurrentPosition();

                _currentPlaying = movie;
                _currentSourceIndex = 0;
                _currentSeason = 1;
                _currentEpisode = 1;
                _currentPositionKey = GetPositionKey(movie);
                _currentUrls = _scraper.GetAllEmbedUrls(movie.Id, movie.MediaType, _currentSeason, _currentEpisode);

                EpisodeBtn.Visibility = movie.IsTv ? Visibility.Visible : Visibility.Collapsed;
                PlayerTitle.Text = $"▶ {movie.Title} ({movie.Year})";
                PlayerOverlay.Visibility = Visibility.Visible;
                PlayerOverlay.Focus();

                FullscreenBtn.Content = "⛶ Fullscreen";
                await InitializeAndNavigate(movie);
            }
        }

        private void EnterFullscreen()
        {
            if (_isFullscreen) return;
            if (Window.GetWindow(this) is not MainWindow mainWindow) return;

            _isFullscreen = true;
            _fullscreenWindow = mainWindow;

            // Hide page content behind the fullscreen layer
            HeaderSection.Visibility = Visibility.Collapsed;
            RecommendedSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;

            // Reparent the player host (WebView2 + close button) into the window's
            // fullscreen layer so it can truly fill the screen, outside the ScrollViewer.
            _fullscreenWindow.HostMediaFullscreen(PlayerHost, PlayerControlsBorder);

            _fullscreenWindow.MediaFullscreenEscapePressed += FullscreenWindow_EscapePressed;
            _fullscreenWindow.EnterMediaFullscreen();
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            _isFullscreen = false;

            HeaderSection.Visibility = Visibility.Visible;
            RecommendedSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Visible;

            if (_fullscreenWindow != null)
            {
                _fullscreenWindow.MediaFullscreenEscapePressed -= FullscreenWindow_EscapePressed;
                // Reparent the player host back into the inline player border
                if (PlayerHost.Parent is Panel oldParent)
                    oldParent.Children.Remove(PlayerHost);
                else if (PlayerHost.Parent is Decorator oldDecorator)
                    oldDecorator.Child = null;
                if (PlayerControlsBorder.Parent is Panel oldCtrlParent)
                    oldCtrlParent.Children.Remove(PlayerControlsBorder);
                else if (PlayerControlsBorder.Parent is Decorator oldCtrlDec)
                    oldCtrlDec.Child = null;
                PlayerBorder.Child = PlayerHost;
                PlayerControlsBorder.SetValue(Grid.RowProperty, 1);
                PlayerControlsBorder.Margin = new Thickness(0, 8, 0, 0);
                PlayerOverlay.Children.Add(PlayerControlsBorder);

                _fullscreenWindow.ExitMediaFullscreen();
                _fullscreenWindow = null;
            }

            PlayerBorder.Height = 480;
        }

        private void FullscreenWindow_EscapePressed(object? sender, EventArgs e)
        {
            ExitFullscreen();
        }

        private void PlayerOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isFullscreen)
                    ExitFullscreen();
                else
                    ClosePlayer();
            }
            else if (e.Key == Key.F11)
            {
                if (_isFullscreen) ExitFullscreen();
                else EnterFullscreen();
            }
        }

        /// <summary>
        /// Ensure the WebView2 player (CoreWebView2) is ready and the ad-blocking
        /// script/filters are installed. Safe to call multiple times — setup runs once.
        /// Pre-warmed at page load so playback starts instantly.
        /// </summary>
        private async Task EnsurePlayerInitializedAsync()
        {
            if (_webViewInitialized) return;
            try
            {
                var userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpokysProjectVercel", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await MoviePlayer.EnsureCoreWebView2Async(env);
                if (!_webViewInitialized)
                {
                    _webViewInitialized = true;
                    MoviePlayer.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
                    MoviePlayer.CoreWebView2.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                    MoviePlayer.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                    MoviePlayer.CoreWebView2.NewWindowRequested += (s, args) => { args.Handled = true; };
                    MoviePlayer.CoreWebView2.PermissionRequested += (s, args) => { args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny; };

                    await MoviePlayer.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        "window.open=function(){return null};" +
                        "window.alert=function(){};" +
                        "window.confirm=function(){return false};" +
                        "window.prompt=function(){return null};" +
                        "window.onbeforeunload=null;" +
                        "window.addEventListener('beforeunload',function(e){e.stopImmediatePropagation()},true);" +
                        "Object.defineProperty(document,'hidden',{get:function(){return false}});" +
                        "Object.defineProperty(document,'visibilityState',{get:function(){return 'visible'}});" +
                        "window.focus=function(){};window.blur=function(){};"
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading player: {ex.Message}", "Player Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task InitializeAndNavigate(MovieResult movie)
        {
            await EnsurePlayerInitializedAsync();
            NavigateToSource();
        }

        private void NavigateToSource()
        {
            if (_currentUrls.Count == 0) return;
            if (_currentSourceIndex >= _currentUrls.Count) _currentSourceIndex = 0;

            PlayerStatusText.Text = "Loading...";
            PlayerStatusText.Visibility = Visibility.Visible;

            MoviePlayer.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            MoviePlayer.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            
            var url = _currentUrls[_currentSourceIndex];
            MoviePlayer.CoreWebView2.Navigate(url);
            
            string sourceInfo = _currentUrls.Count > 1 ? $" (Source {_currentSourceIndex + 1}/{_currentUrls.Count})" : "";
            string episodeInfo = _currentPlaying?.IsTv == true ? $" S{_currentSeason:D2}E{_currentEpisode:D2}" : "";
            PlayerTitle.Text = $"▶ {_currentPlaying?.Title} ({_currentPlaying?.Year}){episodeInfo}{sourceInfo}";
        }

        private async void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            MoviePlayer.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            if (!e.IsSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation failed to {_currentUrls[_currentSourceIndex]}: {e.WebErrorStatus}");
                PlayerStatusText.Text = $"Couldn't reach source {_currentSourceIndex + 1}";
                await AutoAdvanceSource();
                return;
            }
            PlayerStatusText.Visibility = Visibility.Collapsed;
            await SeekToPosition();
            StartPositionTimer();
        }

        private async Task AutoAdvanceSource()
        {
            if (_currentUrls.Count <= 1)
            {
                PlayerStatusText.Text = "Source unreachable";
                return;
            }
            _currentSourceIndex++;
            if (_currentSourceIndex >= _currentUrls.Count)
            {
                PlayerTitle.Text = "⚠ All sources unreachable";
                PlayerStatusText.Text = "All sources unreachable — try a different movie or search again";
                return;
            }
            NavigateToSource();
        }

        private string GetPositionKey(MovieResult movie) =>
            movie.IsTv ? $"{movie.Id}_tv_S{_currentSeason:D2}E{_currentEpisode:D2}" : $"{movie.Id}_movie";

        private async Task SaveCurrentPosition()
        {
            if (string.IsNullOrEmpty(_currentPositionKey) || MoviePlayer.CoreWebView2 == null) return;
            try
            {
                var result = await MoviePlayer.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var v=document.querySelector('video');return v?v.currentTime:0})()");
                if (double.TryParse(result, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pos) && pos > 0)
                {
                    _playbackPositions[_currentPositionKey] = pos;
                }
            }
            catch { }
        }

        private async Task SeekToPosition()
        {
            if (string.IsNullOrEmpty(_currentPositionKey) || MoviePlayer.CoreWebView2 == null) return;
            if (_playbackPositions.TryGetValue(_currentPositionKey, out double pos) && pos > 5)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    try
                    {
                        await Dispatcher.InvokeAsync(() =>
                            MoviePlayer.CoreWebView2?.ExecuteScriptAsync(
                                $"(function(){{var v=document.querySelector('video');if(v){{v.currentTime={pos.ToString(System.Globalization.CultureInfo.InvariantCulture)};}}}})()"));
                    }
                    catch { }
                });
            }
        }

        private void StartPositionTimer()
        {
            StopPositionTimer();
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _positionTimer.Tick += async (s, e) => await SaveCurrentPosition();
            _positionTimer.Start();
        }

        private void StopPositionTimer()
        {
            if (_positionTimer != null)
            {
                _positionTimer.Stop();
                _positionTimer = null;
            }
        }

        private async void PausePlayback()
        {
            if (MoviePlayer.CoreWebView2 == null) return;
            try
            {
                await MoviePlayer.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var v=document.querySelector('video');if(v)v.pause()})()");
                await SaveCurrentPosition();
            }
            catch { }
        }

        private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                PausePlayback();
            }
        }

        private void OnWebResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = e.Request.Uri.ToLowerInvariant();
                foreach (var domain in BlockedDomains)
                {
                    if (uri.Contains(domain))
                    {
                        e.Response = MoviePlayer.CoreWebView2?.Environment?.CreateWebResourceResponse(null, 204, "Blocked", "Content-Length: 0");
                        return;
                    }
                }
                if (uri.Contains("/ads/") || uri.Contains("/ad/") || uri.Contains("/advert") ||
                    uri.Contains("pop.js") || uri.Contains("popunder") || uri.Contains("popup") ||
                    uri.Contains("/banner") || uri.Contains("vast.xml") || uri.Contains("vpaid") ||
                    uri.Contains("prebid") || uri.Contains("/sponsor") ||
                    uri.Contains("interstitial") || uri.Contains("overlay") ||
                    uri.Contains("tracking") || uri.Contains("analytics") || uri.Contains("pixel"))
                {
                    e.Response = MoviePlayer.CoreWebView2?.Environment?.CreateWebResourceResponse(null, 204, "Blocked", "Content-Length: 0");
                }
            }
            catch { }
        }

        private async void TryNextSource_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaying == null || _currentUrls.Count == 0) return;
            await SaveCurrentPosition();
            _currentSourceIndex++;
            if (_currentSourceIndex >= _currentUrls.Count) _currentSourceIndex = 0;
            NavigateToSource();
        }

        private async void ShowEpisodeSelector_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaying == null || !_currentPlaying.IsTv) return;

            var dialog = new Window
            {
                Title = "Select Season & Episode",
                Width = 350,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            var seasonLabel = new TextBlock { Text = "Season:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            var seasonBox = new TextBox { Text = _currentSeason.ToString(), Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 40)), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 10) };
            var epLabel = new TextBlock { Text = "Episode:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            var epBox = new TextBox { Text = _currentEpisode.ToString(), Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 40)), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 10) };
            var playBtn = new Button
            {
                Content = "▶ Play",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 5, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                Foreground = System.Windows.Media.Brushes.White
            };
            playBtn.Click += async (s, ev) =>
            {
                if (int.TryParse(seasonBox.Text, out int sNum) && int.TryParse(epBox.Text, out int eNum))
                {
                    await SaveCurrentPosition();
                    _currentSeason = sNum;
                    _currentEpisode = eNum;
                    _currentPositionKey = GetPositionKey(_currentPlaying!);
                    _currentUrls = _scraper.GetAllEmbedUrls(_currentPlaying.Id, _currentPlaying.MediaType, _currentSeason, _currentEpisode);
                    _currentSourceIndex = 0;
                    dialog.Close();
                    NavigateToSource();
                }
            };

            panel.Children.Add(seasonLabel);
            panel.Children.Add(seasonBox);
            panel.Children.Add(epLabel);
            panel.Children.Add(epBox);
            panel.Children.Add(playBtn);
            dialog.Content = panel;
            dialog.ShowDialog();
        }

        private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen) ExitFullscreen();
            else EnterFullscreen();
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            ExitFullscreen();
            ClosePlayer();
        }

        private async void ClosePlayer()
        {
            await SaveCurrentPosition();
            StopPositionTimer();
            PlayerOverlay.Visibility = Visibility.Collapsed;
            try { if (MoviePlayer.CoreWebView2 != null) MoviePlayer.CoreWebView2.NavigateToString("<html><body style='background:#000'></body></html>"); } catch { }
        }
    }
}

