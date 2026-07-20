using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpokysProjectLightning.Models;

namespace SpokysProjectLightning.Services
{
    public class ShopService
    {
        private static readonly string ShopFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "shop.json");

        private static readonly string AppDataShopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "shop.json");

        public List<ShopItem> LoadItems()
        {
            try
            {
                string path = File.Exists(AppDataShopPath) ? AppDataShopPath : ShopFilePath;
                if (!File.Exists(path)) return new();

                var json = JObject.Parse(File.ReadAllText(path));
                var items = new List<ShopItem>();

                foreach (var entry in json)
                {
                    if (entry.Value is not JObject obj) continue;
                    items.Add(new ShopItem
                    {
                        AppId = entry.Key,
                        Name = obj["name"]?.Value<string>() ?? "Unknown",
                        Active = obj["activo"]?.Value<string>()?.ToLower() == "true",
                        HeaderImage = obj["imgCabecera"]?.Value<string>() ?? "",
                        LogoImage = obj["imgLogo"]?.Value<string>() ?? "",
                        VerticalImage = obj["imgVertical"]?.Value<string>() ?? "",
                        NormalPrice = obj["precioNormal"]?.Value<int>() ?? 0,
                        DonorPrice = obj["precioDonadores"]?.Value<int>() ?? 0,
                        Discount = obj["descuento"]?.Value<int>() ?? 0,
                    });
                }

                return items;
            }
            catch
            {
                return new();
            }
        }

        public void SaveItems(List<ShopItem> items)
        {
            try
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

                var dir = Path.GetDirectoryName(AppDataShopPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(AppDataShopPath, json.ToString(Formatting.Indented));
            }
            catch { }
        }

        public void AddOrUpdateItem(ShopItem item)
        {
            var items = LoadItems();
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
        }

        public void RemoveItem(string appId)
        {
            var items = LoadItems();
            items.RemoveAll(i => i.AppId == appId);
            SaveItems(items);
        }
    }
}
