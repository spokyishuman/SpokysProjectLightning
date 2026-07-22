using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpokysProjectVercel.Services;
using SpokysProjectVercel.ViewModels;

namespace SpokysProjectVercel
{
    public partial class MainWindow : Window
    {
        private GridLength? _sidebarWidthBeforeFullscreen;
        private Visibility _sidebarVisibilityBeforeFullscreen;
        private Visibility _titleBarVisibilityBeforeFullscreen;
        private Thickness _scrollMarginBeforeFullscreen;
        private WindowState _windowStateBeforeFullscreen;
        private ResizeMode _resizeModeBeforeFullscreen;
        private Point _dragStartPoint;
        private bool _isDraggingNav;

        public bool IsMediaFullscreen { get; private set; }
        public event EventHandler? MediaFullscreenEscapePressed;

        public static readonly DependencyProperty IsSidebarExpandedProperty =
            DependencyProperty.Register("IsSidebarExpanded", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public bool IsSidebarExpanded
        {
            get => (bool)GetValue(IsSidebarExpandedProperty);
            set => SetValue(IsSidebarExpandedProperty, value);
        }

        private bool _sidebarAnimating;
        private const double SidebarCollapsedWidth = 64;
        private const double SidebarExpandedWidth = 200;

        private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_sidebarAnimating) return;
            _sidebarAnimating = true;
            IsSidebarExpanded = true;
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = SidebarExpandedWidth,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            anim.Completed += (s, a) => _sidebarAnimating = false;
            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_sidebarAnimating) return;
            _sidebarAnimating = true;
            IsSidebarExpanded = false;
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = SidebarCollapsedWidth,
                Duration = TimeSpan.FromSeconds(0.15),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            anim.Completed += (s, a) => _sidebarAnimating = false;
            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PageScrollViewer == null) return;
            var offset = PageScrollViewer.VerticalOffset - (e.Delta / 3);
            if (offset < 0) offset = 0;
            PageScrollViewer.ScrollToVerticalOffset(offset);
            e.Handled = true;
        }

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(iconPath))
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
            }
            catch { }
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewMouseWheel += MainWindow_PreviewMouseWheel;
            InitInstallationQueue();

            if (DataContext is MainViewModel mainVM)
            {
                mainVM.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.BackdropPath))
                        Dispatcher.BeginInvoke(new Action(() => UpdateBackdrop(mainVM.BackdropPath)));
                };
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppMode.Load();
            UpdateSidebarModeUI();
            AppMode.ModeChanged += () => Dispatcher.BeginInvoke(() => UpdateSidebarModeUI());
            TryInitBackdrop();
            ContentRendered += (s, args) => TryInitBackdrop();
            _ = CheckForUpdatesAsync();
            _ = AutoInstallLumaCoreAsync();
        }

        private async Task AutoInstallLumaCoreAsync()
        {
            try
            {
                var lc = new LumaCoreService();
                if (await lc.InstallIfMissingAsync())
                {
                    await Dispatcher.BeginInvoke(() =>
                    {
                        LumaCoreToast.Visibility = Visibility.Visible;
                    });
                }
            }
            catch { }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var ds = new DataService();
                var settings = ds.LoadSettings();
                if (!string.IsNullOrEmpty(settings.UpdateUrl))
                    UpdateService.UpdateCheckUrl = settings.UpdateUrl;

                var updateService = new UpdateService();
                var update = await updateService.CheckForUpdatesAsync();
                if (update == null) return;

                await Dispatcher.BeginInvoke(() =>
                {
                    UpdateToast.Visibility = Visibility.Visible;
                    if (UpdateToast.FindName("UpdateVersionText") is TextBlock verText)
                        verText.Text = $"v{update.Version} available";
                    if (UpdateToast.FindName("UpdateNotesText") is TextBlock notesText)
                        notesText.Text = update.ReleaseNotes;
                    UpdateToast.Tag = update;
                });
            }
            catch { }
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateToast.Tag is not UpdateManifest manifest) return;

            try
            {
                var btn = sender as Button;
                if (btn != null) btn.IsEnabled = false;

                // Update status text
                if (UpdateToast.FindName("UpdateVersionText") is TextBlock vText)
                    vText.Text = "Downloading update...";

                var svc = new UpdateService();
                var progress = new Progress<double>(p =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (UpdateToast.FindName("UpdateProgressBar") is Border progBar)
                            progBar.Width = (UpdateToast.FindName("UpdateProgressTrack") as Border)?.ActualWidth * p / 100 ?? 0;
                        if (UpdateToast.FindName("UpdateVersionText") is TextBlock pt)
                            pt.Text = $"Downloading... {p:F0}%";
                    });
                });

                var zipPath = await svc.DownloadUpdateAsync(manifest.DownloadUrl, progress);
                if (zipPath == null)
                {
                    if (UpdateToast.FindName("UpdateVersionText") is TextBlock et)
                        et.Text = "Download failed";
                    if (btn != null) btn.IsEnabled = true;
                    return;
                }

                if (UpdateToast.FindName("UpdateVersionText") is TextBlock it)
                    it.Text = "Installing update...";

                if (svc.InstallUpdate(zipPath))
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    if (UpdateToast.FindName("UpdateVersionText") is TextBlock ft)
                        ft.Text = "Install failed - try manually";
                    if (btn != null) btn.IsEnabled = true;
                }
            }
            catch
            {
                if (UpdateToast.FindName("UpdateVersionText") is TextBlock et)
                    et.Text = "Update failed";
            }
        }

        private void TryInitBackdrop()
        {
            if (DataContext is not MainViewModel mainVM) return;

            var videoPath = mainVM.BackdropPath;
            if (!string.IsNullOrEmpty(videoPath) && System.IO.File.Exists(videoPath))
            {
                // Apply theme colors from the video regardless of visual playback
                try { VideoPaletteService.ApplyFromVideo(videoPath); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Palette error: {ex.Message}"); }

                if (mainVM.IsBackdropVisible)
                    UpdateBackdrop(videoPath);
            }
        }

        private void Nav_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ItemsControl items && e.OriginalSource is DependencyObject dep)
            {
                var container = FindVisualParent<FrameworkElement>(dep);
                if (container != null)
                {
                    _dragStartPoint = e.GetPosition(items);
                    _isDraggingNav = false;
                }
            }
        }

        private void Nav_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _isDraggingNav = false; return; }
            if (_isDraggingNav) return;

            var items = sender as ItemsControl;
            if (items == null) return;
            var pos = e.GetPosition(items);
            if (Math.Abs(pos.X - _dragStartPoint.X) < 10 && Math.Abs(pos.Y - _dragStartPoint.Y) < 10) return;

            var dep = e.OriginalSource as DependencyObject;
            if (dep == null) return;
            var container = FindVisualParent<FrameworkElement>(dep);
            if (container == null) return;
            var navItem = container.DataContext as NavItem;
            if (navItem == null || navItem.IsPinned) return;

            _isDraggingNav = true;
            DragDrop.DoDragDrop(container, navItem, DragDropEffects.Move);
        }

        private void Nav_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(NavItem))) return;
            var droppedItem = e.Data.GetData(typeof(NavItem)) as NavItem;
            if (droppedItem == null) return;

            var items = sender as ItemsControl;
            if (items == null || items.DataContext is not MainViewModel vm) return;

            var dep = e.OriginalSource as DependencyObject;
            if (dep == null) return;
            var targetContainer = FindVisualParent<FrameworkElement>(dep);
            var targetItem = targetContainer?.DataContext as NavItem;
            if (targetItem == null) return;

            var fromIdx = vm.NavItems.IndexOf(droppedItem);
            var toIdx = vm.NavItems.IndexOf(targetItem);
            if (fromIdx < 0 || toIdx < 0) return;

            vm.MoveNavItem(fromIdx, toIdx);
            _isDraggingNav = false;
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null && child is not T)
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            return child as T;
        }

        public void EnterMediaFullscreen()
        {
            if (IsMediaFullscreen) return;

            IsMediaFullscreen = true;
            _sidebarWidthBeforeFullscreen = AppShell.ColumnDefinitions[0].Width;
            _sidebarVisibilityBeforeFullscreen = Sidebar.Visibility;
            _titleBarVisibilityBeforeFullscreen = TitleBar.Visibility;
            _scrollMarginBeforeFullscreen = PageScrollViewer.Margin;
            _windowStateBeforeFullscreen = WindowState;
            _resizeModeBeforeFullscreen = ResizeMode;

            Sidebar.Visibility = Visibility.Collapsed;
            TitleBar.Visibility = Visibility.Collapsed;
            AppShell.ColumnDefinitions[0].Width = new GridLength(0);
            PageScrollViewer.Margin = new Thickness(0);
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;

            MediaFullscreenLayer.Visibility = Visibility.Visible;
        }

        public void ExitMediaFullscreen()
        {
            if (!IsMediaFullscreen) return;

            IsMediaFullscreen = false;
            Topmost = false;
            ResizeMode = _resizeModeBeforeFullscreen;
            Sidebar.Visibility = _sidebarVisibilityBeforeFullscreen;
            TitleBar.Visibility = _titleBarVisibilityBeforeFullscreen;
            PageScrollViewer.Margin = _scrollMarginBeforeFullscreen;
            if (_sidebarWidthBeforeFullscreen is GridLength sidebarWidth)
                AppShell.ColumnDefinitions[0].Width = sidebarWidth;
            WindowState = _windowStateBeforeFullscreen;

            MediaFullscreenLayer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Move the movie player host + controls bar into the window-level fullscreen
        /// layer so the video fills the whole window (outside the page ScrollViewer).
        /// </summary>
        public void HostMediaFullscreen(FrameworkElement playerHost,
                                        FrameworkElement controlsBar)
        {
            // Detach from current parents (could be Panel, Decorator, ContentPresenter, etc.)
            RemoveFromParent(playerHost);
            RemoveFromParent(controlsBar);

            Grid.SetRow(playerHost, 0);
            MediaFullscreenLayer.Children.Add(playerHost);

            Grid.SetRow(controlsBar, 1);
            MediaFullscreenLayer.Children.Add(controlsBar);
        }

        /// <summary>
        /// Safely detach a FrameworkElement from whatever visual parent it currently has.
        /// </summary>
        private static void RemoveFromParent(FrameworkElement child)
        {
            if (child.Parent is Panel panel)
                panel.Children.Remove(child);
            else if (child.Parent is Decorator decorator)
                decorator.Child = null;
            else if (child.Parent is ContentPresenter presenter)
                presenter.Content = null;
            else if (child.Parent is ItemsControl items)
                items.Items.Remove(child);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsMediaFullscreen && e.Key == Key.Escape)
            {
                e.Handled = true;
                MediaFullscreenEscapePressed?.Invoke(this, EventArgs.Empty);
            }
        }

        private static bool IsVideoFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".wmv" or ".mov";
        }

        private void UpdateBackdrop(string path)
        {
            try
            {
                ImageWallpaper.Source = null;
                ImageWallpaper.Visibility = Visibility.Collapsed;
                BackdropOverlay.ClearValue(VisibilityProperty);

                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    if (DataContext is MainViewModel vm && vm.IsBackdropVisible)
                        vm.IsBackdropVisible = false;
                    HideBackdropPlayer();
                    return;
                }

                if (IsVideoFile(path))
                {
                    BackdropPlayer.Visibility = Visibility.Visible;
                    BackdropPlayer.MediaFailed -= OnBackdropMediaFailed;
                    BackdropPlayer.MediaFailed += OnBackdropMediaFailed;
                    // Ensure MediaElement is in Manual mode so Play/Pause/Stop are allowed
                    BackdropPlayer.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
                    BackdropPlayer.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;
                    BackdropPlayer.Source = new Uri(path);
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => BackdropPlayer.Play()));
                }
                else
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    ImageWallpaper.Source = bitmap;
                    ImageWallpaper.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backdrop error: {ex.Message}");
            }
        }

        private void HideBackdropPlayer()
        {
            try { if (BackdropPlayer.Source != null) { BackdropPlayer.Stop(); BackdropPlayer.Source = null; } }
            catch { }
            BackdropPlayer.Visibility = Visibility.Collapsed;
        }

        private void OnBackdropMediaFailed(object? sender, System.Windows.ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Backdrop: MediaFailed - {e.ErrorException?.Message}");
        }

        private void BackdropPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MediaElement media)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
        }

        // Window drag
        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        // Window Control Handlers
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DismissToast_Click(object sender, RoutedEventArgs e)
        {
            UpdateToast.Visibility = Visibility.Collapsed;
        }

        private void UpdateSidebarModeUI()
        {
            var primary = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryBrush");
            var muted = (System.Windows.Media.Brush)Application.Current.FindResource("MutedForegroundBrush");
            var accent = (System.Windows.Media.Brush)Application.Current.FindResource("SidebarAccentBrush");
            var trans = System.Windows.Media.Brushes.Transparent;

            if (AppMode.UseLumaCore)
            {
                SidebarModeIcon.Text = "⚡";
                SidebarModeText.Text = "LC";
                SidebarModeToggle.Foreground = primary;
                ModeToggleKnob.SetValue(Grid.ColumnProperty, 0);
                ModeToggleKnob.Background = accent;
                ModeToggleLcIcon.Foreground = primary;
                ModeToggleStSide.Opacity = 0.5;
            }
            else
            {
                SidebarModeIcon.Text = "🛠️";
                SidebarModeText.Text = "ST";
                SidebarModeToggle.Foreground = muted;
                ModeToggleKnob.SetValue(Grid.ColumnProperty, 1);
                ModeToggleKnob.Background = accent;
                ModeToggleLcIcon.Foreground = muted;
                ModeToggleStSide.Opacity = 1.0;
            }
        }

        private async void SidebarModeToggle_Click(object sender, RoutedEventArgs e)
        {
            await SetLumaCoreMode(!AppMode.UseLumaCore);
        }

        private async void ModeToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            await SetLumaCoreMode(!AppMode.UseLumaCore);
        }

        private async Task SetLumaCoreMode(bool useLc)
        {
            AppMode.SetLumaCore(useLc);
            UpdateSidebarModeUI();

            if (AppMode.UseLumaCore)
            {
                if (!LumaCoreService.IsLumaCoreInstalled())
                {
                    var lc = new LumaCoreService();
                    try { await lc.InstallIfMissingAsync(); }
                    catch { }
                    if (LumaCoreService.IsLumaCoreInstalled())
                        ToastService.Show("⚡ LumaCore installed automatically!", "success");
                }
            }
        }

        private async void SidebarRestartSteam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("steam"))
                    p.Kill();
                await Task.Delay(2000);
                var steamPath = SteamService.FindSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var exe = Path.Combine(steamPath, "steam.exe");
                    if (File.Exists(exe))
                        Process.Start(exe);
                }
            }
            catch { }
        }

        private void DismissLumaCoreToast_Click(object sender, RoutedEventArgs e)
        {
            LumaCoreToast.Visibility = Visibility.Collapsed;
        }

        private async void RestartSteam_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                foreach (var p in Process.GetProcessesByName("steam"))
                    p.Kill();

                await Task.Delay(2000);

                var steamPath = SteamService.FindSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var exe = Path.Combine(steamPath, "steam.exe");
                    if (File.Exists(exe))
                        Process.Start(exe);
                }

                LumaCoreToast.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart Steam:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            InstallationManager.Instance.ClearCompleted();
            if (!InstallationManager.Instance.HasActiveTasks)
                QueuePanel.Visibility = Visibility.Collapsed;
        }

        private void InitInstallationQueue()
        {
            var mgr = InstallationManager.Instance;
            mgr.PropertyChanged += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(UpdateQueueUI));
            };
            mgr.Tasks.CollectionChanged += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(UpdateQueueUI));
            };
        }

        private void UpdateQueueUI()
        {
            var mgr = InstallationManager.Instance;
            var hasActive = mgr.HasActiveTasks;
            QueuePanel.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
            QueueItems.ItemsSource = mgr.Tasks;
            var total = mgr.Tasks.Count;
            var done = 0;
            foreach (var t in mgr.Tasks)
            {
                if (t.IsComplete) done++;
            }
            var pct = total > 0 ? (double)done / total * 100 : 0;
            OverallProgressFill.Width = OverallProgressBar.ActualWidth * pct / 100;
            QueueStatusText.Text = total > 0 ? $"{done}/{total} complete ({pct:F0}%)" : "";
        }
    }
}

