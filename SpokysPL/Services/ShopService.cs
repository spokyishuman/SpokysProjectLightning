using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    public class ShopService
    {
        private static readonly string LocalShopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysProjectVercel", "shop.json");

        private static readonly string FallbackShopPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "shop.json");

        private readonly HttpClient _http;

        public ShopService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "SpokysProjectVercel-Shop/1.0");
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        public string SyncStatus { get; private set; } = "";

        public async Task<List<ShopItem>> LoadItemsAsync()
        {
            var settings = new DataService().LoadSettings();
            var remoteUrl = settings.ShopRemoteUrl;

            if (!string.IsNullOrEmpty(remoteUrl))
            {
                try
                {
                    var json = await _http.GetStringAsync(remoteUrl);
                    var items = ParseItems(json);
                    if (items.Count > 0)
                    {
                        SaveLocal(items);
                        SyncStatus = $"✅ Synced — {items.Count} items";
                        return items;
                    }
                }
                catch
                {
                    SyncStatus = "⚠️ Offline — using local cache";
                }
            }

            return LoadLocal();
        }

        public List<ShopItem> LoadItems()
        {
            var items = LoadLocal();
            SyncStatus = items.Count > 0 ? $"📄 Local — {items.Count} items" : "📄 Local — empty";
            return items;
        }

        public async Task<bool> PublishToGithubAsync()
        {
            var settings = new DataService().LoadSettings();
            var token = settings.ShopGithubToken;
            if (string.IsNullOrEmpty(token))
            {
                SyncStatus = "❌ No GitHub token configured";
                return false;
            }

            try
            {
                var items = LoadLocal();
                var content = JsonConvert.SerializeObject(ToDict(items), Formatting.Indented);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

                var repo = "spokyishuman/Spoky-s-Project-Vercel";
                var path = "shop.json";
                var branch = "main";

                var getUrl = $"https://api.github.com/repos/{repo}/contents/{path}?ref={branch}";
                _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var getResp = await _http.GetAsync(getUrl);
                string? sha = null;
                if (getResp.IsSuccessStatusCode)
                {
                    var existing = JObject.Parse(await getResp.Content.ReadAsStringAsync());
                    sha = existing["sha"]?.Value<string>();
                }

                var putData = new JObject
                {
                    ["message"] = "Update shop.json via SpokysProjectVercel",
                    ["content"] = base64,
                    ["branch"] = branch,
                };
                if (sha != null)
                    putData["sha"] = sha;

                var putUrl = $"https://api.github.com/repos/{repo}/contents/{path}";
                var putContent = new StringContent(putData.ToString(), Encoding.UTF8, "application/json");
                var putResp = await _http.PutAsync(putUrl, putContent);

                if (putResp.IsSuccessStatusCode)
                {
                    SyncStatus = "✅ Published to GitHub!";
                    return true;
                }
                else
                {
                    var err = await putResp.Content.ReadAsStringAsync();
                    SyncStatus = $"❌ GitHub error: {putResp.StatusCode}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                SyncStatus = $"❌ Error: {ex.Message}";
                return false;
            }
        }

        public void SaveItems(List<ShopItem> items)
        {
            try
            {
                var dir = Path.GetDirectoryName(LocalShopPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LocalShopPath, JsonConvert.SerializeObject(ToDict(items), Formatting.Indented));
            }
            catch { }
        }

        public void AddOrUpdateItem(ShopItem item)
        {
            var items = LoadLocal();
            var existing = items.FirstOrDefault(i => i.AppId == item.AppId);
            if (existing != null)
            {
                existing.Name = item.Name;
                existing.Active = item.Active;
                existing.HeaderImage = item.HeaderImage;
                existing.LogoImage = item.LogoImage;
                existing.VerticalImage = item.VerticalImage;
                existing.NormalPrice = item.NormalPrice;
                existing.DonorPrice = item.DonorPrice;
                existing.Discount = item.Discount;
            }
            else
            {
                items.Add(item);
            }
            SaveItems(items);
            _ = AutoPublishAsync();
        }

        public void RemoveItem(string appId)
        {
            var items = LoadLocal();
            items.RemoveAll(i => i.AppId == appId);
            SaveItems(items);
            _ = AutoPublishAsync();
        }

        private async Task AutoPublishAsync()
        {
            var settings = new DataService().LoadSettings();
            if (string.IsNullOrEmpty(settings.ShopGithubToken)) return;
            await PublishToGithubAsync();
        }

        private List<ShopItem> LoadLocal()
        {
            try
            {
                string path = File.Exists(LocalShopPath) ? LocalShopPath : FallbackShopPath;
                if (!File.Exists(path)) return new();
                return ParseItems(File.ReadAllText(path));
            }
            catch
            {
                return new();
            }
        }

        private void SaveLocal(List<ShopItem> items)
        {
            try
            {
                var dir = Path.GetDirectoryName(LocalShopPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LocalShopPath, JsonConvert.SerializeObject(ToDict(items), Formatting.Indented));
            }
            catch { }
        }

        private static List<ShopItem> ParseItems(string json)
        {
            var items = new List<ShopItem>();
            var obj = JObject.Parse(json);
            foreach (var entry in obj)
            {
                if (entry.Value is not JObject jo) continue;
                items.Add(new ShopItem
                {
                    AppId = entry.Key,
                    Name = jo["name"]?.Value<string>() ?? "Unknown",
                    Active = jo["activo"]?.Value<string>()?.ToLower() == "true",
                    HeaderImage = jo["imgCabecera"]?.Value<string>() ?? "",
                    LogoImage = jo["imgLogo"]?.Value<string>() ?? "",
                    VerticalImage = jo["imgVertical"]?.Value<string>() ?? "",
                    NormalPrice = jo["precioNormal"]?.Value<int>() ?? 0,
                    DonorPrice = jo["precioDonadores"]?.Value<int>() ?? 0,
                    Discount = jo["descuento"]?.Value<int>() ?? 0,
                });
            }
            return items;
        }

        private static JObject ToDict(List<ShopItem> items)
        {
            var json = new JObject();
            foreach (var item in items)
            {
                json[item.AppId] = new JObject
                {
                    ["name"] = item.Name,
                    ["activo"] = item.Active ? "true" : "false",
                    ["imgCabecera"] = item.HeaderImage ?? "",
                    ["imgLogo"] = item.LogoImage ?? "",
                    ["imgVertical"] = item.VerticalImage ?? "",
                    ["precioNormal"] = item.NormalPrice,
                    ["precioDonadores"] = item.DonorPrice,
                    ["descuento"] = item.Discount,
                };
            }
            return json;
        }
    }
}
