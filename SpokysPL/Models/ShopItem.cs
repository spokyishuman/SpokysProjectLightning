using Newtonsoft.Json;

namespace SpokysProjectVercel.Models
{
    public class ShopItem
    {
        public string AppId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Active { get; set; } = true;
        public string HeaderImage { get; set; } = "";
        public string LogoImage { get; set; } = "";
        public string VerticalImage { get; set; } = "";
        public int NormalPrice { get; set; }
        public int DonorPrice { get; set; }
        public int Discount { get; set; }

        [JsonIgnore]
        public string PriceDisplay => $"${(NormalPrice / 100.0):F2}";

        [JsonIgnore]
        public string DonorPriceDisplay => DonorPrice > 0 ? $"${(DonorPrice / 100.0):F2}" : "";

        [JsonIgnore]
        public bool HasDiscount => Discount > 0;

        [JsonIgnore]
        public string DiscountDisplay => HasDiscount ? $"-{Discount}%" : "";
    }
}
