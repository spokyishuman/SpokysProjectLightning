using System;
using System.Net;
using System.Windows;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure TLS 1.2+ for all HTTP requests
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            // Set up exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                var msg = ex?.Message ?? "Unknown error";
                while (ex?.InnerException != null)
                {
                    ex = ex.InnerException;
                    msg += $"\n\n→ {ex.GetType().Name}: {ex.Message}";
                }
                MessageBox.Show(msg, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                var msg = ex.Message;
                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    msg += $"\n\n→ {ex.GetType().Name}: {ex.Message}";
                }
                msg += $"\n\nStack:\n{args.Exception.StackTrace}";
                MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // Load settings and apply theme + custom colors
            try
            {
                var dataService = new DataService();
                var settings = dataService.LoadSettings();

                // Apply saved theme
                if (!string.IsNullOrEmpty(settings.Theme))
                {
                    var themeService = new ThemeService();
                    themeService.ApplyTheme(settings.Theme);
                }

                // Apply custom color overrides
                if (settings.CustomColors?.Count > 0)
                    ColorCustomizationService.ApplyCustomColors(settings.CustomColors);
            }
            catch { }
        }
    }
}

