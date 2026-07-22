using System;

namespace SpokysProjectVercel.Services
{
    public static class AppMode
    {
        public static bool UseLumaCore { get; private set; } = true;
        public static event Action? ModeChanged;

        public static void Load()
        {
            try
            {
                var settings = new DataService().LoadSettings();
                UseLumaCore = settings.UseLumaCore;
            }
            catch
            {
                UseLumaCore = true;
            }
        }

        public static void SetLumaCore(bool value)
        {
            if (UseLumaCore == value) return;
            UseLumaCore = value;
            try
            {
                var settings = new DataService().LoadSettings();
                settings.UseLumaCore = value;
                new DataService().SaveSettings(settings);
            }
            catch { }
            ModeChanged?.Invoke();
        }
    }
}
