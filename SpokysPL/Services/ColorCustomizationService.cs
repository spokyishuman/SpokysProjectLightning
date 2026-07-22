using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SpokysProjectVercel.Services
{
    public class ColorOption
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string DefaultHex { get; set; } = "";
        public string CurrentHex { get; set; } = "";
    }

    public class ColorCustomizationService
    {
        private static readonly List<ColorOption> _colorOptions = new()
        {
            new() { Key = "AccentBrush", DisplayName = "Accent", Category = "Accent", DefaultHex = "#E94560" },
            new() { Key = "AccentGlowBrush", DisplayName = "Accent Glow", Category = "Accent", DefaultHex = "#FF6B7B" },
            new() { Key = "BackgroundBrush", DisplayName = "Background", Category = "Background", DefaultHex = "#0D0D1A" },
            new() { Key = "SurfaceBrush", DisplayName = "Surface", Category = "Background", DefaultHex = "#1A1A2E" },
            new() { Key = "CardBrush", DisplayName = "Card", Category = "Background", DefaultHex = "#1A1A30" },
            new() { Key = "CardHoverBrush", DisplayName = "Card Hover", Category = "Background", DefaultHex = "#252545" },
            new() { Key = "SidebarBrush", DisplayName = "Sidebar", Category = "Background", DefaultHex = "#0F0F1E" },
            new() { Key = "SidebarAccentBrush", DisplayName = "Sidebar Accent", Category = "Background", DefaultHex = "#222244" },
            new() { Key = "CardBorderBrush", DisplayName = "Card Border", Category = "Borders", DefaultHex = "#2A2A4A" },
            new() { Key = "BorderBrush", DisplayName = "Border", Category = "Borders", DefaultHex = "#2A2A4A" },
            new() { Key = "SidebarBorder", DisplayName = "Sidebar Border", Category = "Borders", DefaultHex = "#2A2A4A" },
            new() { Key = "TextPrimaryBrush", DisplayName = "Text Primary", Category = "Text", DefaultHex = "#F0F0F0" },
            new() { Key = "TextSecondaryBrush", DisplayName = "Text Secondary", Category = "Text", DefaultHex = "#9090A8" },
            new() { Key = "TextMutedBrush", DisplayName = "Text Muted", Category = "Text", DefaultHex = "#606078" },
            new() { Key = "MutedForegroundBrush", DisplayName = "Muted Foreground", Category = "Text", DefaultHex = "#606078" },
            new() { Key = "CardForegroundBrush", DisplayName = "Card Foreground", Category = "Text", DefaultHex = "#F0F0F0" },
            new() { Key = "SuccessBrush", DisplayName = "Success", Category = "Status", DefaultHex = "#4CAF50" },
            new() { Key = "WarningBrush", DisplayName = "Warning", Category = "Status", DefaultHex = "#FF9800" },
            new() { Key = "ErrorBrush", DisplayName = "Error", Category = "Status", DefaultHex = "#F44336" },
            new() { Key = "InfoBrush", DisplayName = "Info", Category = "Status", DefaultHex = "#2196F3" },
        };

        public static List<ColorOption> GetOptions() => _colorOptions;

        public static List<string> GetCategories() => _colorOptions.Select(c => c.Category).Distinct().ToList();

        public static void ApplyCustomColors(Dictionary<string, string> overrides)
        {
            foreach (var (key, hex) in overrides)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);

                    Brush? existing = Application.Current.Resources[key] as Brush;
                    if (existing is LinearGradientBrush gradient)
                    {
                        if (key == "BackgroundBrush")
                        {
                            gradient.GradientStops[0] = new GradientStop(color, 0.0);
                            var r = (byte)(color.R * 0.5);
                            var g = (byte)(color.G * 0.5);
                            var b = (byte)(color.B * 0.5);
                            gradient.GradientStops[1] = new GradientStop(Color.FromRgb(r, g, b), 1.0);
                        }
                        else if (key == "SidebarBrush")
                        {
                            gradient.GradientStops[0] = new GradientStop(color, 0.0);
                            var r = (byte)(color.R * 0.85);
                            var g = (byte)(color.G * 0.85);
                            var b = (byte)(color.B * 0.85);
                            gradient.GradientStops[1] = new GradientStop(Color.FromRgb(r, g, b), 1.0);
                        }
                    }
                    else if (existing is SolidColorBrush solid)
                    {
                        solid.Color = color;
                    }
                    else
                    {
                        // Create new solid brush as fallback
                        Application.Current.Resources[key] = new SolidColorBrush(color);
                    }
                }
                catch { }
            }
        }

        public static string GetDefaultHex(string key)
        {
            var opt = _colorOptions.FirstOrDefault(c => c.Key == key);
            return opt?.DefaultHex ?? "#FFFFFF";
        }
    }
}

