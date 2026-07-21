using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SpokysProjectLightning.Services;
using SpokysProjectLightning.Views;

namespace SpokysProjectLightning.ViewModels
{
    public class NavItem : INotifyPropertyChanged
    {
        public string Icon { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public string PageName { get; set; } = "";
        public string DisplayName { get; set; } = "";

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        public bool IsPinned { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly NavigationService _navigation;
        private readonly ThemeService _themeService;
        private readonly DataService _dataService;
        private readonly DownloadService _downloadService;

        private object _currentPage;
        private bool _isSidebarCollapsed;
        private string _selectedNavItem = "Home";
        private string _statusMessage = "Ready";
        private bool _isBackdropVisible;
        private string _backdropPath = "";
        private double _backdropOverlayOpacity = 0.5;

        public ObservableCollection<NavItem> NavItems { get; } = new()
        {
            new() { Icon = "🏠", ToolTip = "Home", PageName = "Home", DisplayName = "Home", IsPinned = true },
            new() { Icon = "➕", ToolTip = "Add", PageName = "Add", DisplayName = "Add", IsPinned = true },
            new() { Icon = "🔒", ToolTip = "Manage", PageName = "Manage", DisplayName = "Manage", IsPinned = true },
            new() { Icon = "🛡️", ToolTip = "Fixes", PageName = "Fixes", DisplayName = "Fixes", IsPinned = true },
            new() { Icon = "🎬", ToolTip = "Movies", PageName = "Movies", DisplayName = "Movies", IsPinned = true },
            new() { Icon = "🔧", ToolTip = "Tools", PageName = "Tools", DisplayName = "Tools", IsPinned = true },
            new() { Icon = "🛒", ToolTip = "Shop", PageName = "Shop", DisplayName = "Shop", IsPinned = true },
            new() { Icon = "⚡", ToolTip = "OpenSteamTool", PageName = "OpenSteamTool", DisplayName = "OST", IsPinned = true },
            new() { Icon = "🐛", ToolTip = "Report Bug", PageName = "ReportBug", DisplayName = "Bug", IsPinned = true },
        };

        public MainViewModel()
        {
            _navigation = new NavigationService();
            _themeService = new ThemeService();
            _dataService = new DataService();
            _downloadService = new DownloadService();

            // Load settings
            var settings = _dataService.LoadSettings();
            _themeService.ApplyTheme(settings.Theme);
            if (settings.CustomColors?.Count > 0)
                ColorCustomizationService.ApplyCustomColors(settings.CustomColors);
            CurrentTheme = settings.Theme;

            // Auto-detect a background video in the app directory (any mp4/webm/mkv...).
            // If the user hasn't explicitly disabled it, pick the best one and enable it.
            var appDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(settings.BackdropPath) || !System.IO.File.Exists(settings.BackdropPath))
            {
                var detected = VideoPaletteService.FindBackgroundVideo();
                if (!string.IsNullOrEmpty(detected))
                {
                    _backdropPath = detected;
                    _isBackdropVisible = true;
                    settings.BackdropPath = detected;
                    settings.UseVideoBackdrop = true;
                    _dataService.SaveSettings(settings);
                }
                else
                {
                    _backdropPath = "";
                    _isBackdropVisible = false;
                }
            }
            else
            {
                _backdropPath = settings.BackdropPath;
                _isBackdropVisible = settings.UseVideoBackdrop;
            }

            // Initialize pages
            string? pageError = null;
            try { HomePage = new HomePage(); } catch (Exception ex) { pageError = $"HomePage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { AddPage = new AddPage(); } catch (Exception ex) { pageError = $"AddPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { ManagePage = new ManagePage(); } catch (Exception ex) { pageError = $"ManagePage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { FixesPage = new FixesPage(); } catch (Exception ex) { pageError = $"FixesPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { ToolsPage = new ToolsPage(); } catch (Exception ex) { pageError = $"ToolsPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { SettingsPage = new SettingsPage(); } catch (Exception ex) { pageError = $"SettingsPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { MoviesPage = new MoviesPage(); } catch (Exception ex) { pageError = $"MoviesPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { OnlineFixPage = new OnlineFixPage(); } catch (Exception ex) { pageError = $"OnlineFixPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { LibraryPage = new LibraryPage(); } catch (Exception ex) { pageError = $"LibraryPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { BypassPage = new BypassPage(); } catch (Exception ex) { pageError = $"BypassPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { ShopPage = new ShopPage(); } catch (Exception ex) { pageError = $"ShopPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { OpenSteamToolPage = new Views.OpenSteamToolPage(); } catch (Exception ex) { pageError = $"OpenSteamToolPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError == null) try { ReportBugPage = new Views.ReportBugPage(); } catch (Exception ex) { pageError = $"ReportBugPage: {ex.Message}"; System.Windows.MessageBox.Show(pageError); }
            if (pageError != null) throw new InvalidOperationException($"Failed to create page: {pageError}");

            _currentPage = HomePage;

            // Initialize commands
            NavigateCommand = new RelayCommand(Navigate);
            ToggleSidebarCommand = new RelayCommand(_ => ToggleSidebar());

            // Set initial active nav item
            foreach (var ni in NavItems)
                ni.IsActive = string.Equals(ni.PageName, "Home", StringComparison.OrdinalIgnoreCase);
        }

        public HomePage HomePage { get; } = null!;
        public AddPage AddPage { get; } = null!;
        public ManagePage ManagePage { get; } = null!;
        public FixesPage FixesPage { get; } = null!;
        public ToolsPage ToolsPage { get; } = null!;
        public SettingsPage SettingsPage { get; } = null!;

        // Legacy pages (still accessible)
        public MoviesPage MoviesPage { get; } = null!;
        public OnlineFixPage OnlineFixPage { get; } = null!;
        public LibraryPage LibraryPage { get; } = null!;
        public BypassPage BypassPage { get; } = null!;
        public ShopPage ShopPage { get; } = null!;
        public Views.OpenSteamToolPage OpenSteamToolPage { get; } = null!;
        public Views.ReportBugPage ReportBugPage { get; } = null!;

        public object CurrentPage
        {
            get => _currentPage;
            set { _currentPage = value; OnPropertyChanged(); }
        }

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set { _isSidebarCollapsed = value; OnPropertyChanged(); }
        }

        public string SelectedNavItem
        {
            get => _selectedNavItem;
            set { _selectedNavItem = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string CurrentTheme { get; set; } = "Dark";

        public bool IsBackdropVisible
        {
            get => _isBackdropVisible;
            set { if (_isBackdropVisible != value) { _isBackdropVisible = value; OnPropertyChanged(); } }
        }

        public string BackdropPath
        {
            get => _backdropPath;
            set { if (_backdropPath != value) { _backdropPath = value; OnPropertyChanged(); } }
        }

        public double BackdropOverlayOpacity
        {
            get => _backdropOverlayOpacity;
            set { _backdropOverlayOpacity = value; OnPropertyChanged(); }
        }

        public ICommand NavigateCommand { get; }
        public ICommand ToggleSidebarCommand { get; }

        public void Navigate(object? parameter)
        {
            if (parameter is string page)
            {
                SelectedNavItem = page;
                // Update active state on all nav items
                foreach (var ni in NavItems)
                    ni.IsActive = string.Equals(ni.PageName, page, StringComparison.OrdinalIgnoreCase);
                switch (page)
                {
                    case "Home":       CurrentPage = HomePage; break;
                    case "Add":        CurrentPage = AddPage; break;
                    case "Manage":     CurrentPage = ManagePage; break;
                    case "Fixes":      CurrentPage = FixesPage; break;
                    case "Tools":      CurrentPage = ToolsPage; break;
                    case "Settings":
                        CurrentPage = SettingsPage;
                        if (SettingsPage is Views.SettingsPage sp)
                            sp.DataContext = this;
                        break;
                    // Legacy redirects
                    case "Movies":     CurrentPage = MoviesPage; break;
                    case "OnlineFix":
                    case "Bypass":     CurrentPage = FixesPage; break;
                    case "Library":    CurrentPage = ManagePage; break;
                    case "Shop":           CurrentPage = ShopPage; break;
                    case "OpenSteamTool":  CurrentPage = OpenSteamToolPage; break;
                    case "ReportBug":      CurrentPage = ReportBugPage; break;
                }
            }
        }

        public void MoveNavItem(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= NavItems.Count) return;
            if (toIndex < 0 || toIndex >= NavItems.Count) return;
            if (fromIndex == toIndex) return;
            NavItems.Move(fromIndex, toIndex);
        }

        private void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }

        public void ApplyTheme(string themeName)
        {
            CurrentTheme = themeName;
            _themeService.ApplyTheme(themeName);
            var settings = _dataService.LoadSettings();
            settings.Theme = themeName;
            if (settings.CustomColors?.Count > 0)
                ColorCustomizationService.ApplyCustomColors(settings.CustomColors);
            _dataService.SaveSettings(settings);
        }

        public void SetBackdrop(string path)
        {
            BackdropPath = path;
            IsBackdropVisible = !string.IsNullOrEmpty(path);
            var settings = _dataService.LoadSettings();
            settings.BackdropPath = path;
            settings.UseVideoBackdrop = IsBackdropVisible;
            _dataService.SaveSettings(settings);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Simple relay command
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}

