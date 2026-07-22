using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SpokysProjectVercel.Models;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public partial class ShopPage : UserControl
    {
        private readonly ShopService _shop;
        private List<ShopItem> _items = new();

        public ShopPage()
        {
            InitializeComponent();
            _shop = new ShopService();
            Loaded += async (_, _) => await LoadItemsAsync();
        }

        private async System.Threading.Tasks.Task LoadItemsAsync()
        {
            _items = (await _shop.LoadItemsAsync()).Where(i => i.Active).ToList();
            ShopGrid.ItemsSource = _items;

            if (_items.Count == 0)
            {
                ShopGrid.ItemsSource = null;
            }
        }

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
            var dialog = new Window
            {
                Title = "Shop Admin",
                Width = 700,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black
            };

            var margin16 = new Thickness(16, 16, 16, 16);
            var margin0_0_0_12 = new Thickness(0, 0, 0, 12);
            var margin0_0_0_10 = new Thickness(0, 0, 0, 10);
            var margin0_0_0_4 = new Thickness(0, 0, 0, 4);
            var margin10_0_0_10 = new Thickness(10, 0, 0, 10);
            var margin0_8_0_0 = new Thickness(0, 8, 0, 0);
            var pad14_8 = new Thickness(14, 8, 14, 8);
            var pad6_4 = new Thickness(6, 4, 6, 4);
            var pad10_6 = new Thickness(10, 6, 10, 6);

            var white = System.Windows.Media.Brushes.White;
            var black = System.Windows.Media.Brushes.Black;
            var gray = System.Windows.Media.Brushes.Gray;

            var panel = new StackPanel { Margin = margin16 };

            var header = new TextBlock
            {
                Text = "🛒 Shop Management",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = white,
                Margin = margin0_0_0_12
            };

            var bg30 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 40));
            var bg40 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 50));

            // Sync status
            var syncRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin0_0_0_10 };
            var syncIcon = new TextBlock { Text = "🌐", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var syncText = new TextBlock
            {
                Text = _shop.SyncStatus,
                FontSize = 12,
                Foreground = gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            syncRow.Children.Add(syncIcon);
            syncRow.Children.Add(syncText);

            var addBtn = new Button
            {
                Content = "➕ Add Game by App ID",
                Padding = pad14_8,
                Margin = margin0_0_0_10,
                FontSize = 13,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                Foreground = white,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var listBox = new ListBox
            {
                Height = 300,
                DisplayMemberPath = "DisplayText",
                Background = bg30,
                Foreground = white,
                BorderBrush = gray
            };

            void RefreshList()
            {
                items = _shop.LoadItems();
                listBox.ItemsSource = items.Select(i => new
                {
                    i.AppId,
                    DisplayText = $"[{(i.Active ? "✓" : "✗")}] {i.Name} (App {i.AppId}) — ${(i.NormalPrice / 100.0):F2} / ${(i.DonorPrice / 100.0):F2}"
                }).ToList();
                syncText.Text = _shop.SyncStatus;
            }

            addBtn.Click += (_, _) =>
            {
                var input = new Window
                {
                    Title = "Add Game",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = dialog,
                    Background = black,
                    ResizeMode = ResizeMode.NoResize
                };
                var inputPanel = new StackPanel { Margin = margin16 };

                var lbl1 = new TextBlock { Text = "Steam App ID:", Foreground = white, Margin = margin0_0_0_4 };
                var appIdBox = new TextBox
                {
                    Text = "292030",
                    Foreground = white,
                    Background = bg40,
                    Padding = pad6_4,
                    Margin = margin0_0_0_10
                };
                var lbl2 = new TextBlock { Text = "Name:", Foreground = white, Margin = margin0_0_0_4 };
                var nameBox = new TextBox
                {
                    Foreground = white,
                    Background = bg40,
                    Padding = pad6_4,
                    Margin = margin0_0_0_10
                };
                var fetchBtn = new Button
                {
                    Content = "🔍 Fetch from Steam",
                    Padding = pad10_6,
                    Margin = margin0_0_0_10,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                    Foreground = white,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var lbl3 = new TextBlock { Text = "Normal Price (cents):", Foreground = white, Margin = margin0_0_0_4 };
                var priceBox = new TextBox
                {
                    Text = "10000",
                    Foreground = white,
                    Background = bg40,
                    Padding = pad6_4,
                    Margin = margin0_0_0_10
                };
                var lbl4 = new TextBlock { Text = "Donor Price (cents):", Foreground = white, Margin = margin0_0_0_4 };
                var donorBox = new TextBox
                {
                    Text = "7500",
                    Foreground = white,
                    Background = bg40,
                    Padding = pad6_4,
                    Margin = margin0_0_0_10
                };
                var saveBtn = new Button
                {
                    Content = "💾 Save",
                    Padding = pad14_8,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                    Foreground = white,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsEnabled = false
                };

                fetchBtn.Click += async (_, _) =>
                {
                    var id = appIdBox.Text.Trim();
                    if (string.IsNullOrEmpty(id)) return;
                    fetchBtn.IsEnabled = false;
                    fetchBtn.Content = "⏳ Fetching...";
                    try
                    {
                        var url = $"https://store.steampowered.com/api/appdetails?appids={id}";
                        using var http = new System.Net.Http.HttpClient();
                        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                        var json = await http.GetStringAsync(url);
                        var data = Newtonsoft.Json.Linq.JObject.Parse(json);
                        var appData = data[id]?["data"];
                        if (appData != null)
                        {
                            nameBox.Text = (string)appData["name"] ?? "";
                            saveBtn.IsEnabled = true;
                        }
                        else
                        {
                            MessageBox.Show("Game not found on Steam.", "Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error: {ex.Message}", "Error");
                    }
                    finally
                    {
                        fetchBtn.IsEnabled = true;
                        fetchBtn.Content = "🔍 Fetch from Steam";
                    }
                };

                saveBtn.Click += (_, _) =>
                {
                    if (!int.TryParse(appIdBox.Text.Trim(), out var appId)) return;
                    if (!int.TryParse(priceBox.Text.Trim(), out var price)) price = 0;
                    if (!int.TryParse(donorBox.Text.Trim(), out var donor)) donor = 0;

                    _shop.AddOrUpdateItem(new ShopItem
                    {
                        AppId = appId.ToString(),
                        Name = nameBox.Text.Trim(),
                        Active = true,
                        NormalPrice = price,
                        DonorPrice = donor,
                        HeaderImage = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg",
                        LogoImage = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/logo.png",
                        VerticalImage = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
                    });

                    RefreshList();
                    _ = LoadItemsAsync();
                    input.Close();
                };

                inputPanel.Children.Add(lbl1);
                inputPanel.Children.Add(appIdBox);
                inputPanel.Children.Add(fetchBtn);
                inputPanel.Children.Add(lbl2);
                inputPanel.Children.Add(nameBox);
                inputPanel.Children.Add(lbl3);
                inputPanel.Children.Add(priceBox);
                inputPanel.Children.Add(lbl4);
                inputPanel.Children.Add(donorBox);
                inputPanel.Children.Add(saveBtn);
                input.Content = inputPanel;
                input.ShowDialog();
            };

            var deleteBtn = new Button
            {
                Content = "🗑 Delete Selected",
                Padding = pad14_8,
                Margin = margin10_0_0_10,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
                Foreground = white,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            deleteBtn.Click += (_, _) =>
            {
                if (listBox.SelectedItem == null) return;
                var selected = listBox.SelectedItem;
                var appId = selected.GetType().GetProperty("AppId")?.GetValue(selected)?.ToString();
                if (appId != null)
                {
                    _shop.RemoveItem(appId);
                    RefreshList();
                    _ = LoadItemsAsync();
                }
            };

            // Publish button
            var publishBtn = new Button
            {
                Content = "📤 Publish to GitHub",
                Padding = pad14_8,
                FontSize = 13,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 39, 176)),
                Foreground = white,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            publishBtn.Click += async (_, _) =>
            {
                var settings = new DataService().LoadSettings();
                var token = settings.ShopGithubToken;
                if (string.IsNullOrEmpty(token))
                {
                    var setupToken = MessageBox.Show(
                        "No GitHub token configured. Set one now in Settings?",
                        "GitHub Token", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                if (ok)
                    MessageBox.Show("Shop data published to GitHub!\nUsers will see updates on next refresh.", "Published", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Failed to publish.\n{_shop.SyncStatus}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            btnPanel.Children.Add(addBtn);
            btnPanel.Children.Add(deleteBtn);
            btnPanel.Children.Add(publishBtn);

            var statusText = new TextBlock
            {
                Text = $"Total items: {items.Count}",
                Foreground = gray,
                FontSize = 12,
                Margin = margin0_8_0_0
            };

            RefreshList();

            panel.Children.Add(header);
            panel.Children.Add(syncRow);
            panel.Children.Add(btnPanel);
            panel.Children.Add(listBox);
            panel.Children.Add(statusText);
            dialog.Content = panel;
            dialog.ShowDialog();
        }
    }
}
