using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace SpokysProjectLightning.Services
{
    public class ThemeService
    {
        private readonly Dictionary<string, Uri> _themes = new()
        {
            ["Dark"] = new Uri("/Themes/DarkTheme.xaml", UriKind.Relative),
            ["Light"] = new Uri("/Themes/LightTheme.xaml", UriKind.Relative),
            ["Midnight Blue"] = new Uri("/Themes/MidnightBlueTheme.xaml", UriKind.Relative),
            ["Emerald"] = new Uri("/Themes/EmeraldTheme.xaml", UriKind.Relative),
            ["Royal Purple"] = new Uri("/Themes/PurpleTheme.xaml", UriKind.Relative)
        };

        public string CurrentTheme { get; private set; } = "Emerald";

        public List<string> AvailableThemes => new(_themes.Keys);

        public void ApplyTheme(string themeName)
        {
            if (!_themes.ContainsKey(themeName)) return;

            CurrentTheme = themeName;

            // Remove existing theme dictionaries
            var mergedDicts = Application.Current.Resources.MergedDictionaries;
            var toRemove = new List<ResourceDictionary>();
            foreach (var dict in mergedDicts)
            {
                if (dict.Source != null && dict.Source.OriginalString.Contains("Theme"))
                    toRemove.Add(dict);
            }
            foreach (var dict in toRemove)
                mergedDicts.Remove(dict);

            // Add the new theme
            try
            {
                var newDict = new ResourceDictionary { Source = _themes[themeName] };
                mergedDicts.Add(newDict);
            }
            catch
            {
                // Fallback to dark theme
                if (themeName != "Dark")
                {
                    try
                    {
                        var fallbackDict = new ResourceDictionary { Source = _themes["Dark"] };
                        mergedDicts.Add(fallbackDict);
                    }
                    catch { }
                }
            }
        }
    }
}

