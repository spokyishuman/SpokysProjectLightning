using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
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
        public ToolsPage()
        {
            InitializeComponent();
            Loaded += ToolsPage_Loaded;
        }

        private void ToolsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateModeBadge();
            AppMode.ModeChanged += () => Dispatcher.BeginInvoke(() => UpdateModeBadge());

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

        private void UpdateModeBadge()
        {
            if (AppMode.UseLumaCore)
            {
                ModeBadgeText.Text = "⚡ LC";
                ModeBadge.Background = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryBrush");
            }
            else
            {
                ModeBadgeText.Text = "🛠️ ST";
                ModeBadge.Background = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush");
            }
        }

        private void ToolCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open {url}:\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
