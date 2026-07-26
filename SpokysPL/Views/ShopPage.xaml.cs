using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SpokysProjectVercel.Models;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public partial class ShopPage : UserControl
    {
        private readonly ShopService _shop;
        private List<ShopItem> _items = new();
        private List<ShopItem> _filtered = new();
        private string _currentFilter = "";

        public ShopPage()
        {
            InitializeComponent();
            _shop = new ShopService();
            Loaded += async (_, _) => await LoadItemsAsync();
        }

        private async System.Threading.Tasks.Task LoadItemsAsync()
        {
            _items = (await _shop.LoadItemsAsync()).Where(i => i.Active).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";

            _filtered = _items.Where(i =>
            {
                if (!string.IsNullOrEmpty(_currentFilter) &&
                    !i.ItemType.Equals(_currentFilter, StringComparison.OrdinalIgnoreCase) &&
                    !(_currentFilter == "Other" && !IsKnownType(i.ItemType)))
                    return false;

                if (!string.IsNullOrEmpty(q) && !i.Name.ToLowerInvariant().Contains(q) &&
                    !i.Description.ToLowerInvariant().Contains(q))
                    return false;

                return true;
            }).ToList();

            ShopGrid.ItemsSource = _filtered;
            EmptyState.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = _filtered.Count == 0 && _items.Count > 0
                ? "No items match your filter."
                : "No items available yet.";
            ItemCountText.Text = $"{_filtered.Count} / {_items.Count} items";
            UpdateFilterHighlight();
        }

        private static bool IsKnownType(string t) => t is "Steam Game" or "Account" or "Game Key" or "Service";

        private void UpdateFilterHighlight()
        {
            foreach (var child in FilterPanel.Children)
            {
                if (child is Border b && b.Tag is string tag)
                {
                    b.Background = tag.Equals(_currentFilter, StringComparison.OrdinalIgnoreCase) ||
                        (_currentFilter == "" && tag == "")
                        ? TryFindResource("PrimaryBrush") as Brush ?? Brushes.DeepSkyBlue
                        : TryFindResource("SurfaceBrush") as Brush ?? Brushes.Gray;
                    if (b.Child is TextBlock tb)
                        tb.Foreground = Brushes.White;
                }
            }
            FilterAllBtn.Background = string.IsNullOrEmpty(_currentFilter)
                ? TryFindResource("PrimaryBrush") as Brush ?? Brushes.DeepSkyBlue
                : TryFindResource("SurfaceBrush") as Brush ?? Brushes.Gray;
        }

        private void FilterBtn_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
            {
                _currentFilter = _currentFilter == tag ? "" : tag;
                ApplyFilter();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        // ── Admin Panel ──

        private void ManageBtn_Click(object sender, RoutedEventArgs e)
        {
            var settings = new DataService().LoadSettings();
            var adminPassword = settings.ShopAdminPassword;

            if (string.IsNullOrEmpty(adminPassword))
            {
                var setup = new PasswordDialog("Set Admin Password",
                    "Create a password to manage the shop:", "");
                setup.Owner = Window.GetWindow(this);
                if (setup.ShowDialog() != true) return;
                adminPassword = setup.Password;
                if (string.IsNullOrEmpty(adminPassword)) return;
                settings.ShopAdminPassword = adminPassword;
                new DataService().SaveSettings(settings);
            }

            var login = new PasswordDialog("Admin Access",
                "Enter admin password to manage shop:", "");
            login.Owner = Window.GetWindow(this);
            if (login.ShowDialog() != true) return;
            if (login.Password != adminPassword)
            {
                MessageBox.Show("Incorrect password.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenAdminPanel();
        }

        private void OpenAdminPanel()
        {
            var items = _shop.LoadItems();
            var w = Window.GetWindow(this);
            var dialog = new Window
            {
                Title = "Shop Admin",
                Width = 820,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = w,
                Background = TryFindResource("BackgroundBrush") as Brush ?? Brushes.Black,
                Style = null
            };
            if (w.Resources.MergedDictionaries.Count > 0)
                dialog.Resources.MergedDictionaries.Add(w.Resources.MergedDictionaries[0]);

            T R<T>(string key) where T : class => TryFindResource(key) as T;
            var bgCard = R<Brush>("CardBrush") ?? new SolidColorBrush(Color.FromRgb(30, 30, 45));
            var bgSurface = R<Brush>("SurfaceBrush") ?? new SolidColorBrush(Color.FromRgb(25, 25, 35));
            var textPrimary = R<Brush>("TextPrimaryBrush") ?? Brushes.White;
            var textSecondary = R<Brush>("TextSecondaryBrush") ?? Brushes.Gray;
            var borderBrush = R<Brush>("BorderBrush") ?? textSecondary;

            var margin16 = new Thickness(16);
            var pad14_8 = new Thickness(14, 8, 14, 8);
            var pad10_6 = new Thickness(10, 6, 10, 6);

            var panel = new StackPanel { Margin = margin16 };

            // ── Header row with title + status ──
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition());
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.Children.Add(new TextBlock
            {
                Text = "🛒 Shop Management",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = textPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            var statusText = new TextBlock
            {
                Text = $"Total: {items.Count} items",
                FontSize = 12,
                Foreground = textSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            Grid.SetColumn(statusText, 1);
            headerRow.Children.Add(statusText);

            // ── Sync status row ──
            var syncRow = new Border
            {
                Background = bgSurface,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 8, 0, 10),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            };
            var syncInner = new StackPanel { Orientation = Orientation.Horizontal };
            var syncIcon = new TextBlock { Text = "🌐", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var syncText = new TextBlock
            {
                Text = _shop.SyncStatus,
                FontSize = 12,
                Foreground = textSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            syncInner.Children.Add(syncIcon);
            syncInner.Children.Add(syncText);
            syncRow.Child = syncInner;

            // ── Search + Refresh row ──
            var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchBox = new TextBox
            {
                Background = bgSurface,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                FontSize = 12,
                Padding = new Thickness(8, 5, 8, 5)
            };
            var refreshBtn = new Button
            {
                Content = "🔄",
                Background = bgSurface,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 14,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Refresh items from cloud"
            };
            var clearFilterBtn = new Button
            {
                Content = "✕",
                Background = bgSurface,
                Foreground = textSecondary,
                BorderBrush = borderBrush,
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Clear search",
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(searchBox, 0);
            Grid.SetColumn(refreshBtn, 1);
            Grid.SetColumn(clearFilterBtn, 2);
            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(refreshBtn);
            searchRow.Children.Add(clearFilterBtn);

            // ── Item list with details ──
            var listBox = new ListBox
            {
                Height = 300,
                Background = bgSurface,
                Foreground = textPrimary,
                BorderBrush = borderBrush,
                Margin = new Thickness(0, 0, 0, 10),
                ItemTemplate = null
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

            // Custom item template for rich list items
            void RefreshList(string filter = "")
            {
                items = _shop.LoadItems();
                var filtered = string.IsNullOrWhiteSpace(filter)
                    ? items.ToList()
                    : items.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || i.AppId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || i.ItemType.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                listBox.ItemsSource = filtered;
                listBox.DisplayMemberPath = null;
                listBox.ItemTemplate = CreateItemTemplate(bgCard, bgSurface, textPrimary, textSecondary, borderBrush);
                syncText.Text = _shop.SyncStatus;
                statusText.Text = $"Total: {items.Count} items · showing {filtered.Count}";
                clearFilterBtn.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Collapsed : Visibility.Visible;
            }

            // ── Buttons ──
            var addGameBtn = MakeButton("🎮 Add Steam Game", Brushes.DodgerBlue, pad14_8);
            var addCustomBtn = MakeButton("📦 Add Custom Item", Brushes.Orange, pad14_8);
            var editBtn = MakeButton("✏️ Edit Selected", Brushes.Gray, pad14_8);
            var deleteBtn = MakeButton("🗑 Delete Selected", Brushes.Crimson, pad14_8);
            var publishBtn = MakeButton("📤 Publish to GitHub", new SolidColorBrush(Color.FromRgb(156, 39, 176)), pad14_8);

            var btnRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            btnRow1.Children.Add(addGameBtn);
            btnRow1.Children.Add(addCustomBtn);
            btnRow1.Children.Add(editBtn);
            btnRow1.Children.Add(deleteBtn);

            var btnRow2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            btnRow2.Children.Add(publishBtn);

            // ── Event handlers ──

            searchBox.TextChanged += (_, _) => RefreshList(searchBox.Text);
            clearFilterBtn.Click += (_, _) => { searchBox.Text = ""; };

            refreshBtn.Click += async (_, _) =>
            {
                refreshBtn.Content = "⏳";
                await _shop.LoadItemsAsync();
                RefreshList(searchBox.Text);
                await LoadItemsAsync();
                refreshBtn.Content = "🔄";
            };

            addGameBtn.Click += (_, _) => ShowAddItemDialog(dialog, "Steam Game", _shop, () => RefreshList(searchBox.Text), () => _ = LoadItemsAsync());
            addCustomBtn.Click += (_, _) => ShowAddItemDialog(dialog, "Custom", _shop, () => RefreshList(searchBox.Text), () => _ = LoadItemsAsync());

            editBtn.Click += (_, _) =>
            {
                if (listBox.SelectedItem is ShopItem sel)
                    ShowEditItemDialog(dialog, sel, _shop, () => RefreshList(searchBox.Text), () => _ = LoadItemsAsync());
                else
                    MessageBox.Show("Select an item first.", "Edit");
            };

            deleteBtn.Click += (_, _) =>
            {
                if (listBox.SelectedItem is ShopItem sel)
                {
                    var confirm = MessageBox.Show(
                        $"Delete \"{sel.Name}\" (ID: {sel.AppId})?\n\nThis will be removed from the cloud when you publish.",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                    _shop.RemoveItem(sel.AppId);
                    RefreshList(searchBox.Text);
                    _ = LoadItemsAsync();
                }
            };

            publishBtn.Click += async (_, _) =>
            {
                var settings = new DataService().LoadSettings();
                var token = settings.ShopGithubToken;
                if (string.IsNullOrEmpty(token))
                {
                    var setupToken = MessageBox.Show(
                        "No GitHub token configured. Set one now?", "GitHub Token",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (setupToken != MessageBoxResult.Yes) return;
                    var tokenDialog = new PasswordDialog("GitHub Token",
                        "Enter a GitHub Personal Access Token (repo scope):", "");
                    tokenDialog.Owner = dialog;
                    if (tokenDialog.ShowDialog() != true) return;
                    token = tokenDialog.Password;
                    if (string.IsNullOrEmpty(token)) return;
                    settings.ShopGithubToken = token;
                    new DataService().SaveSettings(settings);
                }
                publishBtn.IsEnabled = false;
                publishBtn.Content = "⏳ Publishing...";
                var ok = await _shop.PublishToGithubAsync();
                publishBtn.IsEnabled = true;
                publishBtn.Content = "📤 Publish to GitHub";
                syncText.Text = _shop.SyncStatus;
                MessageBox.Show(ok ? "✅ Published!" : $"❌ {_shop.SyncStatus}", ok ? "Success" : "Error");
            };

            RefreshList();

            panel.Children.Add(headerRow);
            panel.Children.Add(syncRow);
            panel.Children.Add(searchRow);
            panel.Children.Add(listBox);
            panel.Children.Add(btnRow1);
            panel.Children.Add(btnRow2);
            dialog.Content = new ScrollViewer { Content = panel };
            dialog.ShowDialog();
        }

        private DataTemplate CreateItemTemplate(Brush cardBg, Brush surfaceBg, Brush textPrimary, Brush textSecondary, Brush border)
        {
            return TryFindResource("AdminItemTemplate") as DataTemplate
                ?? CreateCodeItemTemplate(cardBg, surfaceBg, textPrimary, textSecondary, border);
        }

        private static DataTemplate CreateCodeItemTemplate(Brush cardBg, Brush surfaceBg, Brush textPrimary, Brush textSecondary, Brush border)
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.BackgroundProperty, cardBg);
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            factory.SetValue(Border.PaddingProperty, new Thickness(10));
            factory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 4));
            factory.SetValue(Border.BorderBrushProperty, border);
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var iconText = new FrameworkElementFactory(typeof(TextBlock));
            iconText.SetBinding(TextBlock.TextProperty, new Binding("TypeBadge"));
            iconText.SetValue(TextBlock.FontSizeProperty, 18.0);
            iconText.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0));
            iconText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            sp.AppendChild(iconText);

            var infoStack = new FrameworkElementFactory(typeof(StackPanel));
            infoStack.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameText.SetValue(TextBlock.FontSizeProperty, 14.0);
            nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            nameText.SetValue(TextBlock.ForegroundProperty, textPrimary);
            infoStack.AppendChild(nameText);
            var idText = new FrameworkElementFactory(typeof(TextBlock));
            idText.SetBinding(TextBlock.TextProperty, new Binding("AppId"));
            idText.SetValue(TextBlock.FontSizeProperty, 11.0);
            idText.SetValue(TextBlock.ForegroundProperty, textSecondary);
            infoStack.AppendChild(idText);
            sp.AppendChild(infoStack);

            var priceText = new FrameworkElementFactory(typeof(TextBlock));
            priceText.SetBinding(TextBlock.TextProperty, new Binding("PriceDisplay"));
            priceText.SetValue(TextBlock.FontSizeProperty, 14.0);
            priceText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            priceText.SetValue(TextBlock.ForegroundProperty, textPrimary);
            priceText.SetValue(TextBlock.MarginProperty, new Thickness(20, 0, 0, 0));
            priceText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            priceText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            sp.AppendChild(priceText);

            factory.AppendChild(sp);
            return new DataTemplate { VisualTree = factory };
        }

        private static Button MakeButton(string text, Brush bg, Thickness pad)
        {
            return new Button
            {
                Content = text,
                Padding = pad,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                Background = bg,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
        }

        // ── Add Item Dialog ──

        private static void ShowAddItemDialog(Window owner, string mode, ShopService shop,
            Action refresh, Action reloadPage)
        {
            var isSteam = mode == "Steam Game";
            var dialog = new Window
            {
                Title = isSteam ? "Add Steam Game" : "Add Custom Item",
                Width = 440,
                Height = isSteam ? 480 : 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = owner.Background,
                ResizeMode = ResizeMode.NoResize,
                Style = null
            };
            if (owner.Resources.MergedDictionaries.Count > 0)
                dialog.Resources.MergedDictionaries.Add(owner.Resources.MergedDictionaries[0]);

            var bgCard = owner.TryFindResource("CardBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45));
            var bgSurface = owner.TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(25, 25, 35));
            var textPrimary = owner.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
            var textSecondary = owner.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            var borderBrush = owner.TryFindResource("BorderBrush") as Brush ?? textSecondary;

            var m = new Thickness(16);
            var pad6 = new Thickness(6, 4, 6, 4);
            var pad10 = new Thickness(10, 6, 10, 6);

            var panel = new StackPanel { Margin = m };

            TextBlock MakeLbl(string t) => new() { Text = t, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 3), FontSize = 12 };
            TextBox MakeBox() => new() { Foreground = textPrimary, Background = bgCard, Padding = pad6, Margin = new Thickness(0, 0, 0, 10), BorderBrush = borderBrush, FontSize = 12 };

            var typeBox = MakeBox();
            if (isSteam) typeBox.Text = "Steam Game";
            var typeSelector = new ComboBox
            {
                ItemsSource = new[] { "Steam Game", "Account", "Game Key", "Service", "Other" },
                SelectedItem = isSteam ? "Steam Game" : "Custom",
                Foreground = textPrimary,
                Background = bgCard,
                Padding = pad6,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };

            var appIdBox = MakeBox();
            appIdBox.Text = "292030";
            appIdBox.Visibility = isSteam ? Visibility.Visible : Visibility.Collapsed;
            var appIdLbl = MakeLbl("Steam App ID:");
            appIdLbl.Visibility = appIdBox.Visibility;

            var nameBox = MakeBox();
            var descBox = MakeBox();
            descBox.Height = 50;
            descBox.TextWrapping = TextWrapping.Wrap;
            descBox.AcceptsReturn = true;

            var priceBox = MakeBox(); priceBox.Text = "10000";
            var donorBox = MakeBox(); donorBox.Text = "7500";

            var imageUrlBox = MakeBox();
            imageUrlBox.Visibility = Visibility.Collapsed;
            var imageUrlLbl = MakeLbl("Custom Image URL:");
            imageUrlLbl.Visibility = Visibility.Collapsed;

            var fetchBtn = new Button
            {
                Content = "🔍 Fetch from Steam",
                Padding = pad10,
                Margin = new Thickness(0, 0, 0, 10),
                Background = Brushes.DodgerBlue,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = isSteam ? Visibility.Visible : Visibility.Collapsed
            };

            typeSelector.SelectionChanged += (_, _) =>
            {
                var t = typeSelector.SelectedItem?.ToString() ?? "Custom";
                var isSteamMode = t == "Steam Game";
                appIdBox.Visibility = isSteamMode ? Visibility.Visible : Visibility.Collapsed;
                appIdLbl.Visibility = isSteamMode ? Visibility.Visible : Visibility.Collapsed;
                fetchBtn.Visibility = isSteamMode ? Visibility.Visible : Visibility.Collapsed;
                imageUrlBox.Visibility = isSteamMode ? Visibility.Collapsed : Visibility.Visible;
                imageUrlLbl.Visibility = isSteamMode ? Visibility.Collapsed : Visibility.Visible;
            };

            fetchBtn.Click += async (_, _) =>
            {
                var id = appIdBox.Text.Trim();
                if (string.IsNullOrEmpty(id)) return;
                fetchBtn.IsEnabled = false;
                fetchBtn.Content = "⏳ Fetching...";
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    var json = await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={id}");
                    var data = Newtonsoft.Json.Linq.JObject.Parse(json)[id]?["data"];
                    if (data != null)
                    {
                        nameBox.Text = (string)data["name"] ?? "";
                        var headerImg = (string)data["header_image"] ?? "";
                        imageUrlBox.Text = headerImg;
                    }
                    else
                    {
                        MessageBox.Show("Game not found on Steam.", "Error");
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error"); }
                finally { fetchBtn.IsEnabled = true; fetchBtn.Content = "🔍 Fetch from Steam"; }
            };

            var saveBtn = new Button
            {
                Content = "💾 Save Item",
                Padding = new Thickness(14, 8, 14, 8),
                Background = Brushes.ForestGreen,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 13
            };
            saveBtn.Click += (_, _) =>
            {
                var t = typeSelector.SelectedItem?.ToString() ?? "Custom";
                var isSteamMode = t == "Steam Game";
                var appId = isSteamMode ? appIdBox.Text.Trim() : Guid.NewGuid().ToString("N")[..8];
                if (isSteamMode && !int.TryParse(appId, out _)) { MessageBox.Show("Invalid App ID."); return; }
                if (!int.TryParse(priceBox.Text.Trim(), out var price)) price = 0;
                if (!int.TryParse(donorBox.Text.Trim(), out var donor)) donor = 0;

                var imgUrl = isSteamMode
                    ? $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg"
                    : imageUrlBox.Text.Trim();

                shop.AddOrUpdateItem(new ShopItem
                {
                    AppId = appId,
                    Name = nameBox.Text.Trim(),
                    ItemType = t,
                    Description = descBox.Text.Trim(),
                    Active = true,
                    NormalPrice = price,
                    DonorPrice = donor,
                    HeaderImage = imgUrl,
                    CustomImageUrl = !isSteamMode ? imageUrlBox.Text.Trim() : "",
                });
                refresh();
                reloadPage();
                dialog.Close();
            };

            panel.Children.Add(MakeLbl("Type:"));
            panel.Children.Add(typeSelector);
            panel.Children.Add(appIdLbl);
            panel.Children.Add(appIdBox);
            panel.Children.Add(fetchBtn);
            panel.Children.Add(MakeLbl("Name:"));
            panel.Children.Add(nameBox);
            panel.Children.Add(MakeLbl("Description:"));
            panel.Children.Add(descBox);
            panel.Children.Add(imageUrlLbl);
            panel.Children.Add(imageUrlBox);
            panel.Children.Add(MakeLbl("Normal Price (cents, e.g. 1000 = $10):"));
            panel.Children.Add(priceBox);
            panel.Children.Add(MakeLbl("Donor Price (cents):"));
            panel.Children.Add(donorBox);
            panel.Children.Add(saveBtn);

            dialog.Content = new ScrollViewer { Content = panel };
            dialog.ShowDialog();
        }

        // ── Purchase Flow ──

        private async void BuyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not ShopItem item) return;

            btn.IsEnabled = false;
            btn.Content = "Processing...";

            try
            {
                var w = Window.GetWindow(this);
                var textPrimary = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
                var textSecondary = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
                var bgCard = TryFindResource("CardBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45));
                var borderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;

                var dialog = new Window
                {
                    Title = $"Purchase: {item.Name}",
                    Width = 420,
                    Height = 520,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = w,
                    Background = w?.Background ?? Brushes.Black,
                    Style = null,
                    ResizeMode = ResizeMode.NoResize
                };
                if (w?.Resources.MergedDictionaries.Count > 0)
                    dialog.Resources.MergedDictionaries.Add(w.Resources.MergedDictionaries[0]);

                var panel = new StackPanel { Margin = new Thickness(20) };

                panel.Children.Add(new TextBlock
                {
                    Text = $"{item.TypeBadge}  {item.Name}",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = textPrimary,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    FontSize = 12,
                    Foreground = textSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = $"Price: {item.PriceDisplay}",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = TryFindResource("PrimaryBrush") as Brush ?? Brushes.DodgerBlue,
                    Margin = new Thickness(0, 0, 0, 16)
                });

                // Payment instructions
                var payBox = new Border
                {
                    Background = bgCard,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                var payStack = new StackPanel();
                payStack.Children.Add(new TextBlock
                {
                    Text = "Payment Instructions",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = textPrimary,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                payStack.Children.Add(new TextBlock
                {
                    Text = "1. Send the exact amount via GPay / UPI / any method to:",
                    FontSize = 11,
                    Foreground = textSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                payStack.Children.Add(new TextBlock
                {
                    Text = "spoky@upi",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = textPrimary,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                payStack.Children.Add(new TextBlock
                {
                    Text = "2. Enter your Discord username below so we can deliver your item.",
                    FontSize = 11,
                    Foreground = textSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                payBox.Child = payStack;
                panel.Children.Add(payBox);

                // Discord username input
                panel.Children.Add(new TextBlock
                {
                    Text = "Your Discord Username (e.g., user#1234):",
                    FontSize = 11,
                    Foreground = textSecondary,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                var discordBox = new TextBox
                {
                    Foreground = textPrimary,
                    Background = bgCard,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 16),
                    BorderBrush = borderBrush,
                    FontSize = 13
                };
                panel.Children.Add(discordBox);

                // Confirm button
                var confirmBtn = new Button
                {
                    Content = "I Have Paid — Deliver My Item",
                    Padding = new Thickness(14, 10, 14, 10),
                    Background = Brushes.ForestGreen,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                panel.Children.Add(confirmBtn);

                var statusText = new TextBlock
                {
                    FontSize = 11,
                    Foreground = textSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Visibility = Visibility.Collapsed
                };
                panel.Children.Add(statusText);

                confirmBtn.Click += async (_, _) =>
                {
                    var discord = discordBox.Text.Trim();
                    if (string.IsNullOrEmpty(discord))
                    {
                        MessageBox.Show("Enter your Discord username so we can deliver your item.", "Missing Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    confirmBtn.IsEnabled = false;
                    confirmBtn.Content = "Sending...";
                    statusText.Visibility = Visibility.Visible;
                    statusText.Text = "Processing your order...";
                    statusText.Foreground = textSecondary;

                    // Generate key for premium / key-type items
                    string? generatedKey = null;
                    if (item.ItemType == "Service" || item.ItemType == "Game Key")
                    {
                        generatedKey = PremiumService.GenerateKey();
                    }

                    // Send webhook notification to shop owner
                    var ok = await SendPurchaseWebhook(item, discord, generatedKey);

                    if (ok)
                    {
                        statusText.Text = "Order received! The shop owner will contact you on Discord within 24 hours.";
                        statusText.Foreground = Brushes.ForestGreen;
                        confirmBtn.Content = "Done";

                        // Show the generated key if applicable
                        if (generatedKey != null)
                        {
                            var keyBox = new Border
                            {
                                Background = new SolidColorBrush(Color.FromRgb(20, 40, 20)),
                                CornerRadius = new CornerRadius(6),
                                Padding = new Thickness(12),
                                Margin = new Thickness(0, 8, 0, 0),
                                BorderBrush = Brushes.ForestGreen,
                                BorderThickness = new Thickness(1)
                            };
                            var keyStack = new StackPanel();
                            keyStack.Children.Add(new TextBlock
                            {
                                Text = "Your License Key",
                                FontSize = 12,
                                FontWeight = FontWeights.Bold,
                                Foreground = Brushes.ForestGreen,
                                Margin = new Thickness(0, 0, 0, 6)
                            });
                            keyStack.Children.Add(new TextBlock
                            {
                                Text = generatedKey,
                                FontSize = 18,
                                FontWeight = FontWeights.Bold,
                                Foreground = Brushes.LimeGreen,
                                TextAlignment = TextAlignment.Center
                            });
                            keyStack.Children.Add(new TextBlock
                            {
                                Text = "Enter this key in Settings → Premium to activate.",
                                FontSize = 11,
                                Foreground = Brushes.DarkGray,
                                Margin = new Thickness(0, 6, 0, 0),
                                TextWrapping = TextWrapping.Wrap
                            });
                            keyBox.Child = keyStack;
                            panel.Children.Add(keyBox);
                        }

                    }
                    else
                    {
                        statusText.Text = "Failed to send order. Please try again or contact the shop owner directly.";
                        statusText.Foreground = Brushes.OrangeRed;
                        confirmBtn.IsEnabled = true;
                        confirmBtn.Content = "Retry";
                    }
                };

                dialog.Content = new ScrollViewer { Content = panel };
                dialog.ShowDialog();
            }
            finally
            {
                btn.Content = "Buy Now";
                btn.IsEnabled = true;
            }
        }

        private async Task<bool> SendPurchaseWebhook(ShopItem item, string discordUsername, string? generatedKey)
        {
            try
            {
                var settings = new DataService().LoadSettings();
                var webhookUrl = generatedKey != null && !string.IsNullOrEmpty(settings.PremiumKeyWebhookUrl)
                    ? settings.PremiumKeyWebhookUrl
                    : settings.ShopPurchaseWebhookUrl;
                if (string.IsNullOrWhiteSpace(webhookUrl))
                    webhookUrl = settings.BugReportWebhookUrl;
                if (string.IsNullOrWhiteSpace(webhookUrl)) return false;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**New Purchase Order!**");
                sb.AppendLine($"Item: {item.TypeBadge} **{item.Name}**");
                sb.AppendLine($"Price: {item.PriceDisplay}");
                sb.AppendLine($"Description: {item.Description}");
                sb.AppendLine($"Buyer Discord: **{discordUsername}**");
                if (generatedKey != null)
                    sb.AppendLine($"Generated Key: `{generatedKey}`");
                sb.AppendLine();
                sb.AppendLine("Contact the buyer on Discord to deliver the item.");

                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL-Shop/1.0");
                http.Timeout = TimeSpan.FromSeconds(15);

                var payload = new { content = sb.ToString() };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var resp = await http.PostAsync(webhookUrl,
                    new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ── Edit Item Dialog ──

        private static void ShowEditItemDialog(Window owner, ShopItem item, ShopService shop,
            Action refresh, Action reloadPage)
        {
            var dialog = new Window
            {
                Title = $"Edit: {item.Name}",
                Width = 440,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = owner.Background,
                ResizeMode = ResizeMode.NoResize,
                Style = null
            };
            if (owner.Resources.MergedDictionaries.Count > 0)
                dialog.Resources.MergedDictionaries.Add(owner.Resources.MergedDictionaries[0]);

            var bgCard = owner.TryFindResource("CardBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45));
            var bgSurface = owner.TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(25, 25, 35));
            var textPrimary = owner.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
            var textSecondary = owner.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            var borderBrush = owner.TryFindResource("BorderBrush") as Brush ?? textSecondary;

            var m = new Thickness(16);
            var pad6 = new Thickness(6, 4, 6, 4);

            TextBlock L(string t) => new() { Text = t, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 3), FontSize = 12 };
            TextBox B() => new() { Foreground = textPrimary, Background = bgCard, Padding = pad6, Margin = new Thickness(0, 0, 0, 10), BorderBrush = borderBrush, FontSize = 12 };

            var panel = new StackPanel { Margin = m };

            var typeCb = new ComboBox
            {
                ItemsSource = new[] { "Steam Game", "Account", "Game Key", "Service", "Other" },
                SelectedItem = item.ItemType,
                Foreground = textPrimary, Background = bgCard, Padding = pad6, FontSize = 12, Margin = new Thickness(0, 0, 0, 10)
            };
            var appIdBox = B(); appIdBox.Text = item.AppId; appIdBox.IsEnabled = false;
            var nameBox = B(); nameBox.Text = item.Name;
            var descBox = B(); descBox.Text = item.Description; descBox.Height = 50; descBox.TextWrapping = TextWrapping.Wrap; descBox.AcceptsReturn = true;
            var priceBox = B(); priceBox.Text = item.NormalPrice.ToString();
            var donorBox = B(); donorBox.Text = item.DonorPrice.ToString();
            var imgBox = B(); imgBox.Text = item.CustomImageUrl;
            var activeCb = new CheckBox
            {
                Content = "Active (visible to users)",
                IsChecked = item.Active,
                Foreground = textPrimary,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };

            var saveBtn = new Button
            {
                Content = "💾 Save Changes",
                Padding = new Thickness(14, 8, 14, 8),
                Background = Brushes.ForestGreen,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 13
            };
            saveBtn.Click += (_, _) =>
            {
                if (!int.TryParse(priceBox.Text.Trim(), out var price)) price = item.NormalPrice;
                if (!int.TryParse(donorBox.Text.Trim(), out var donor)) donor = item.DonorPrice;

                item.ItemType = typeCb.SelectedItem?.ToString() ?? item.ItemType;
                item.Name = nameBox.Text.Trim();
                item.Description = descBox.Text.Trim();
                item.NormalPrice = price;
                item.DonorPrice = donor;
                item.CustomImageUrl = imgBox.Text.Trim();
                item.Active = activeCb.IsChecked ?? true;
                shop.AddOrUpdateItem(item);
                refresh();
                reloadPage();
                dialog.Close();
            };

            panel.Children.Add(L("Type:")); panel.Children.Add(typeCb);
            panel.Children.Add(L("App ID:")); panel.Children.Add(appIdBox);
            panel.Children.Add(L("Name:")); panel.Children.Add(nameBox);
            panel.Children.Add(L("Description:")); panel.Children.Add(descBox);
            panel.Children.Add(L("Normal Price (cents):")); panel.Children.Add(priceBox);
            panel.Children.Add(L("Donor Price (cents):")); panel.Children.Add(donorBox);
            panel.Children.Add(L("Custom Image URL (optional):")); panel.Children.Add(imgBox);
            panel.Children.Add(activeCb);
            panel.Children.Add(saveBtn);

            dialog.Content = new ScrollViewer { Content = panel };
            dialog.ShowDialog();
        }

        private async void BuyPremium_Click(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            var textPrimary = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
            var textSecondary = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            var bgCard = TryFindResource("CardBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 45));
            var borderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;

            var dialog = new Window
            {
                Title = "Buy Premium",
                Width = 400,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = w,
                Background = w?.Background ?? Brushes.Black,
                Style = null,
                ResizeMode = ResizeMode.NoResize
            };
            if (w?.Resources.MergedDictionaries.Count > 0)
                dialog.Resources.MergedDictionaries.Add(w.Resources.MergedDictionaries[0]);

            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "👑 Premium License",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = textPrimary,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Remove ads, support development, and get early access to features.",
                FontSize = 12,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Price: $5.00 USD",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = TryFindResource("PrimaryBrush") as Brush ?? Brushes.DodgerBlue,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var payBox = new Border
            {
                Background = bgCard,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var payStack = new StackPanel();
            payStack.Children.Add(new TextBlock
            {
                Text = "Payment Instructions",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = textPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });
            payStack.Children.Add(new TextBlock
            {
                Text = "1. Send $5.00 via GPay / UPI / any method to:",
                FontSize = 11,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            payStack.Children.Add(new TextBlock
            {
                Text = "spoky@upi",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = textPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });
            payStack.Children.Add(new TextBlock
            {
                Text = "2. Enter your Discord username below and click confirm.",
                FontSize = 11,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            payStack.Children.Add(new TextBlock
            {
                Text = "Your premium key will be sent to you via Discord after payment is verified.",
                FontSize = 10,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                FontStyle = FontStyles.Italic
            });
            payBox.Child = payStack;
            panel.Children.Add(payBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Your Discord Username (e.g., user#1234):",
                FontSize = 11,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var discordBox = new TextBox
            {
                Foreground = textPrimary,
                Background = bgCard,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 14),
                BorderBrush = borderBrush,
                FontSize = 13
            };
            panel.Children.Add(discordBox);

            var confirmBtn = new Button
            {
                Content = "I Have Paid — Generate My Key",
                Padding = new Thickness(14, 10, 14, 10),
                Background = Brushes.ForestGreen,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(confirmBtn);

            var statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(statusText);

            confirmBtn.Click += async (_, _) =>
            {
                var discord = discordBox.Text.Trim();
                if (string.IsNullOrEmpty(discord))
                {
                    MessageBox.Show("Enter your Discord username so we can deliver your key.", "Missing Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                confirmBtn.IsEnabled = false;
                confirmBtn.Content = "Processing...";
                statusText.Visibility = Visibility.Visible;
                statusText.Text = "Generating your license key...";
                statusText.Foreground = textSecondary;

                var key = PremiumService.GenerateKey();
                var ok = await SendPremiumPurchaseWebhook(discord, key);

                if (ok)
                {
                    statusText.Text = "Order received! The key will be delivered on Discord within 24 hours.";
                    statusText.Foreground = Brushes.ForestGreen;
                    confirmBtn.Content = "Done";

                    var keyBox = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(20, 40, 20)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 8, 0, 0),
                        BorderBrush = Brushes.ForestGreen,
                        BorderThickness = new Thickness(1)
                    };
                    var keyStack = new StackPanel();
                    keyStack.Children.Add(new TextBlock
                    {
                        Text = "Your License Key (save this!)",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.ForestGreen,
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                    keyStack.Children.Add(new TextBlock
                    {
                        Text = key,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.LimeGreen,
                        TextAlignment = TextAlignment.Center
                    });
                    keyStack.Children.Add(new TextBlock
                    {
                        Text = "Enter this key in Settings → Premium to activate.",
                        FontSize = 11,
                        Foreground = Brushes.DarkGray,
                        Margin = new Thickness(0, 6, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                    keyBox.Child = keyStack;
                    panel.Children.Add(keyBox);
                }
                else
                {
                    statusText.Text = "Failed to submit. Check your connection and try again.";
                    statusText.Foreground = Brushes.OrangeRed;
                    confirmBtn.IsEnabled = true;
                    confirmBtn.Content = "Retry";
                }
            };

            dialog.Content = new ScrollViewer { Content = panel };
            dialog.ShowDialog();
        }

        private async Task<bool> SendPremiumPurchaseWebhook(string discordUsername, string key)
        {
            try
            {
                var settings = new DataService().LoadSettings();
                var webhookUrl = !string.IsNullOrEmpty(settings.ShopPurchaseWebhookUrl)
                    ? settings.ShopPurchaseWebhookUrl
                    : settings.BugReportWebhookUrl;
                if (string.IsNullOrWhiteSpace(webhookUrl)) return false;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("**New Premium Purchase!**");
                sb.AppendLine($"Buyer Discord: **{discordUsername}**");
                sb.AppendLine($"Generated Key: `{key}`");
                sb.AppendLine();
                sb.AppendLine("Deliver the key to the buyer on Discord.");

                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL-Premium/1.0");
                http.Timeout = TimeSpan.FromSeconds(15);

                var payload = new { content = sb.ToString() };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var resp = await http.PostAsync(webhookUrl,
                    new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
