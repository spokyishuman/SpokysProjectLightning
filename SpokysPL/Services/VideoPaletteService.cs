using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SpokysProjectLightning.Services
{
    public static class VideoPaletteService
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".wmv", ".mov", ".m4v", ".mkv", ".avi"
        };

        private static readonly string[] PreferredNames =
            { "background", "backdrop", "bg", "wallpaper", "loop" };

        public static string? FindBackgroundVideo()
        {
            var dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            List<string> videos;
            try
            {
                videos = Directory.EnumerateFiles(dir)
                    .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return null; }

            if (videos.Count == 0) return null;

            foreach (var pref in PreferredNames)
            {
                var match = videos.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Equals(pref, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return videos[0];
        }

        public static Dictionary<string, string> GetPaletteOverrides(string videoPath)
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                    return GetFallbackOverrides();

                var palette = ExtractPaletteFromVideo(videoPath);
                if (palette.Background is not Color bg)
                    return GetFallbackOverrides();

                var accent = palette.Accent ?? Adjust(bg, 0.6);
                var accentGlow = Adjust(accent, 0.4);
                var surface = Adjust(bg, 1.25);
                var card = Adjust(bg, 1.18);
                var cardHover = Adjust(bg, 1.45);
                var sidebar = Adjust(bg, 0.7);
                var border = Adjust(bg, 1.6);

                return new Dictionary<string, string>
                {
                    ["BackgroundBrush"] = ToHex(bg),
                    ["SurfaceBrush"] = ToHex(surface),
                    ["CardBrush"] = ToHex(card),
                    ["CardHoverBrush"] = ToHex(cardHover),
                    ["SidebarBrush"] = ToHex(sidebar),
                    ["AccentBrush"] = ToHex(accent),
                    ["PrimaryBrush"] = ToHex(accent),
                    ["AccentGlowBrush"] = ToHex(accentGlow),
                    ["BorderBrush"] = ToHex(border),
                    ["CardBorderBrush"] = ToHex(border),
                    ["SidebarAccentBrush"] = ToHex(Adjust(surface, 1.1)),
                };
            }
            catch
            {
                return GetFallbackOverrides();
            }
        }

        public static void ApplyFromVideo(string videoPath)
        {
            var overrides = GetPaletteOverrides(videoPath);
            ColorCustomizationService.ApplyCustomColors(overrides);

            var data = new DataService();
            var settings = data.LoadSettings();
            settings.CustomColors = overrides;
            data.SaveSettings(settings);

            // Also update drop-shadow effects if present
            try
            {
                var palette = ExtractPaletteFromVideo(videoPath);
                if (palette.Accent is Color accent)
                {
                    var app = Application.Current;
                    if (app != null)
                    {
                        SetEffectColor(app, "AccentGlowEffect", accent, 0.6);
                        SetEffectColor(app, "ButtonShadow", accent, 0.45);
                        SetEffectColor(app, "TextGlow", accent, 0.55);
                        SetEffectColor(app, "NeonRing", accent, 0.3);
                    }
                }
            }
            catch { }
        }

        private static Dictionary<string, string> GetFallbackOverrides()
        {
            return new Dictionary<string, string>
            {
                ["BackgroundBrush"] = "#121612",
                ["SurfaceBrush"] = "#171C17",
                ["CardBrush"] = "#161E16",
                ["CardHoverBrush"] = "#1D2A1D",
                ["SidebarBrush"] = "#0D100D",
                ["AccentBrush"] = "#4ADE80",
                ["PrimaryBrush"] = "#4ADE80",
                ["AccentGlowBrush"] = "#6BE89B",
                ["BorderBrush"] = "#263326",
                ["CardBorderBrush"] = "#263326",
                ["SidebarAccentBrush"] = "#192619",
            };
        }

        private sealed class Palette
        {
            public Color? Background;
            public Color? Accent;
        }

        /// <summary>
        /// Derive a palette from the video by hashing the file's name and size into
        /// a seed that produces a consistent, pleasant dark colour scheme per video.
        /// Avoids any frame extraction (which requires COM interop or blocking the
        /// UI thread for MediaPlayer to open).
        /// </summary>
        private static Palette ExtractPaletteFromVideo(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return new Palette();

                // Seed from file name + size for a deterministic but video-specific hue
                int seed = fi.Name.GetHashCode(StringComparison.OrdinalIgnoreCase)
                         ^ (int)(fi.Length & 0x7FFFFFFF);
                var rng = new Random(seed);

                // Pick a hue in the 120-200° range (greens through blues)
                double hue = 120 + rng.NextDouble() * 80;
                double sat = 0.35 + rng.NextDouble() * 0.15;   // 35-50%
                double lum = 0.12 + rng.NextDouble() * 0.08;   // 12-20% (dark)

                var bg = ColorFromHsl(hue, sat, lum);

                // Accent: shift hue +-30°, higher saturation and luminance
                double accentHue = hue + (rng.Next(0, 2) == 0 ? 30.0 : -30.0);
                var accent = ColorFromHsl(accentHue, 0.7, 0.55);

                return new Palette { Background = bg, Accent = accent };
            }
            catch
            {
                return new Palette();
            }
        }

        private static Color ColorFromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;

            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private static Color Adjust(Color c, double factor)
        {
            factor = Math.Clamp(factor, 0.05, 4.0);
            byte r = (byte)Math.Clamp(c.R * factor, 0, 255);
            byte g = (byte)Math.Clamp(c.G * factor, 0, 255);
            byte b = (byte)Math.Clamp(c.B * factor, 0, 255);
            return Color.FromRgb(r, g, b);
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static void SetEffectColor(Application app, string key, Color color, double opacity)
        {
            if (app.Resources[key] is not System.Windows.Media.Effects.DropShadowEffect eff) return;
            eff.Color = color;
            eff.Opacity = opacity;
        }
    }
}

