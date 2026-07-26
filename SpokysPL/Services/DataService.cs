using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    public class DataService
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpokysPL");

        private static readonly string DataJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
        private static readonly string DataFixJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-fix.json");
        private static readonly string SettingsPath = Path.Combine(AppDataPath, "settings.json");

        public DataService()
        {
            Directory.CreateDirectory(AppDataPath);

            // Copy original data files if they exist in base directory
            string srcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "PL_Extracted");
            CopyDataFile(srcDir, "data.json");
            CopyDataFile(srcDir, "data-fix.json");
        }

        private void CopyDataFile(string srcDir, string fileName)
        {
            string src = Path.Combine(srcDir, fileName);
            string dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(src) && !File.Exists(dest))
            {
                try { File.Copy(src, dest); }
                catch { }
            }
        }

        /// <summary>
        /// Reads a custom image value that may be either a plain string URL or an object
        /// with hero/poster/hero_image/background members. Returns the first usable URL.
        /// </summary>
        private static string ReadCustomImage(JToken? token, string field)
        {
            if (token == null) return "";
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? "";
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                return obj[field]?.Value<string>()
                    ?? obj["hero"]?.Value<string>()
                    ?? obj["poster"]?.Value<string>()
                    ?? obj["hero_image"]?.Value<string>()
                    ?? obj["background"]?.Value<string>()
                    ?? "";
            }
            return "";
        }

        public Dictionary<string, Dictionary<string, GameInfo>> LoadBypassData()
        {
            var data = new Dictionary<string, Dictionary<string, GameInfo>>();
            try
            {
                string path = DataJsonPath;
                if (!File.Exists(path))
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Downloads", "PL_Extracted", "data.json");
                }
                if (File.Exists(path))
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    foreach (var category in json)
                    {
                        if (category.Value is not JObject catObj) continue;
                        var games = new Dictionary<string, GameInfo>();
                        foreach (var game in catObj)
                        {
                            if (game.Value is not JObject g) continue;
                            var info = new GameInfo
                            {
                                AppId = game.Key,
                                Name = g["name"]?.Value<string>() ?? "Unknown",
                                FixName = g["nombre_fix"]?.Value<string>() ?? "",
                                Category = category.Key,
                                LaunchSteam = g["launch_steam"]?.Value<bool>() ?? false,
                                LaunchExe = g["launch_exe"]?.Value<bool>() ?? false,
                                Comments = g["comentarios"]?.Value<string>() ?? "",
                                HeroImage = ReadCustomImage(g["custom_images"], "hero_image"),
                                Background = ReadCustomImage(g["custom_images"], "background")
                            };

                            if (g["programas_necesarios"] is JArray progs)
                                info.RequiredPrograms = progs.Select(p => p.Value<string>() ?? "").ToList();

                            if (g["errores"] is JArray errors)
                                info.Errors = errors.Select(e => e.Value<string>() ?? "").ToList();

                            games[game.Key] = info;
                        }
                        data[category.Key] = games;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading bypass data: {ex.Message}");
            }
            return data;
        }

        public List<GameInfo> LoadDataFix()
        {
            var games = new List<GameInfo>();
            try
            {
                string path = DataFixJsonPath;
                if (!File.Exists(path))
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Downloads", "PL_Extracted", "data-fix.json");
                }
                if (File.Exists(path))
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    foreach (var game in json)
                    {
                        if (game.Value is not JObject g) continue;
                        games.Add(new GameInfo
                        {
                            AppId = game.Key,
                            Name = g["name"]?.Value<string>() ?? "Unknown",
                            FixName = g["nombre_fix"]?.Value<string>() ?? "",
                            HeroImage = ReadCustomImage(g["custom_images"], "hero"),
                            Category = "Fixes"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading fix data: {ex.Message}");
            }
            return games;
        }

        /// <summary>
        /// Add an online-fix game to the local data-fix.json database.
        /// Stores structured download links, password, version, and image object.
        /// </summary>
        public void AddOnlineFixToDatabase(OnlineFixGame game)
        {
            try
            {
                string path = DataFixJsonPath;
                JObject json;

                if (File.Exists(path))
                {
                    json = JObject.Parse(File.ReadAllText(path));
                }
                else
                {
                    json = new JObject();
                }

                string key = !string.IsNullOrEmpty(game.Id) ? game.Id : Guid.NewGuid().ToString("N").Substring(0, 8);

                // Build the entry (used for both create and update)
                void WriteEntry(JObject target)
                {
                    target["name"] = game.Title;
                    target["nombre_fix"] = $"{game.Title} - online fix";
                    target["online_fix_url"] = game.Url;
                    target["online_fix_category"] = game.Category;
                    target["password"] = game.Password;
                    if (!string.IsNullOrEmpty(game.GameVersion))
                        target["game_version"] = game.GameVersion;
                    if (!string.IsNullOrEmpty(game.FileSize))
                        target["file_size"] = game.FileSize;
                    if (!string.IsNullOrEmpty(game.Description))
                        target["description"] = game.Description;

                    // custom_images as an object (consistent across read paths)
                    if (!string.IsNullOrEmpty(game.ImageUrl))
                        target["custom_images"] = new JObject
                        {
                            ["hero"] = game.ImageUrl,
                            ["poster"] = game.ImageUrl,
                            ["hero_image"] = game.ImageUrl,
                            ["background"] = game.ImageUrl
                        };

                    if (game.DownloadLinks.Count > 0)
                        target["download_links"] = JArray.FromObject(game.DownloadLinks.Select(l => new
                        {
                            name = l.Label,
                            label = l.Label,
                            url = l.Url,
                            type = l.Type
                        }).ToList());
                }

                if (json[key] != null)
                {
                    WriteEntry((JObject)json[key]!);
                }
                else
                {
                    var entry = new JObject();
                    WriteEntry(entry);
                    json[key] = entry;
                }

                File.WriteAllText(path, json.ToString(Formatting.Indented));
                System.Diagnostics.Debug.WriteLine($"Added/Updated online fix: {game.Title} (key: {key})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding online fix to database: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get all online fixes stored in the local database
        /// </summary>
        public List<OnlineFixGame> GetLocalOnlineFixes()
        {
            var games = new List<OnlineFixGame>();
            try
            {
                string path = DataFixJsonPath;
                if (File.Exists(path))
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    foreach (var game in json)
                    {
                        // Only include entries that have online_fix_url (were added from online-fix.me)
                        if (game.Value is JObject g && g["online_fix_url"] != null)
                        {
                            var fix = new OnlineFixGame
                            {
                                Id = game.Key,
                                Title = game.Value["name"]?.Value<string>() ?? "Unknown",
                                Url = game.Value["online_fix_url"]?.Value<string>() ?? "",
                                Category = game.Value["online_fix_category"]?.Value<string>() ?? "",
                                ImageUrl = ReadCustomImage(game.Value["custom_images"], "hero"),
                                Description = game.Value["description"]?.Value<string>() ?? "",
                                IsInstalled = true
                            };
                            games.Add(fix);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading local online fixes: {ex.Message}");
            }
            return games;
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public static string GetDownloadPath()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (s != null && !string.IsNullOrEmpty(s.DownloadPath))
                        return s.DownloadPath;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SpokysPL");
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(AppDataPath);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { }
        }
    }

    public class AppSettings
    {
        public string Theme { get; set; } = "Emerald";
        public string BackdropPath { get; set; } = string.Empty;
        public bool UseVideoBackdrop { get; set; }
        public string SteamPath { get; set; } = string.Empty;
        public string DownloadPath { get; set; } = string.Empty;
        public double BackdropVolume { get; set; } = 30;
        public string Mode { get; set; } = "cloud";
        public string OmdbApiKey { get; set; } = "c80f73ed";
        public string TmdbApiKey { get; set; } = "03ea17fd725585fa30751965ed1993eb";
        public string MovieProxyUrl { get; set; } = "https://spokys-tmdb-proxy.vercel.app";
        public string UpdateUrl { get; set; } = string.Empty;
        public string ShopAdminPassword { get; set; } = string.Empty;
        public string ShopRemoteUrl { get; set; } = "https://raw.githubusercontent.com/spokyishuman/Spoky-s-Project-Vercel/main/shop.json";
        public string ShopGithubToken { get; set; } = string.Empty;
        public string SteamDaddyApiKey { get; set; } = string.Empty;
        public string BugReportWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1529453122022543443/ENAOlLg5N9fLfwe8W5CCdxOmMR1VvGUTYIurm8bHxeMekUt5_cgjWYJtKdEw3ijS5e5M";
        public string ShopPurchaseWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1530127771253215284/IovUb5QJIroN3anCg-HjbeQfLdB56v39HiNgjyZ9BjAhHYH6O5MLKbqihIaAXA4eFStG";
        public string PremiumKeyWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1530127123606536222/ibBJW5-DbLIcBVguEkjKcC1p4eHFbasDa9HU7XzVc_fNXk7XuXD1rJqT15d1XumEpVoF";
        public bool UseLumaCore { get; set; } = true;
        public bool IsPremium { get; set; }
        public string PremiumKey { get; set; } = string.Empty;
        public Dictionary<string, string> CustomColors { get; set; } = new();
    }
}

