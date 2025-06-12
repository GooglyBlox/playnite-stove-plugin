using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace StoveLibrary.Models
{
    public class StoveGameData
    {
        [JsonProperty("product_no")]
        public int ProductNo { get; set; }

        [JsonProperty("product_name")]
        public string ProductName { get; set; }

        [JsonProperty("short_piece")]
        public string ShortPiece { get; set; }

        [JsonProperty("game_id")]
        public string GameId { get; set; }

        [JsonProperty("game_no")]
        public int GameNo { get; set; }

        [JsonProperty("genre_tag_name")]
        public string GenreTagName { get; set; }

        [JsonProperty("shop_item_id")]
        public int ShopItemId { get; set; }

        [JsonProperty("product_type")]
        public string ProductType { get; set; }

        [JsonProperty("platform_types")]
        public List<string> PlatformTypes { get; set; }

        [JsonProperty("restrict_status")]
        public string RestrictStatus { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("sale_status")]
        public string SaleStatus { get; set; }

        [JsonProperty("recommend_count")]
        public int RecommendCount { get; set; }

        [JsonProperty("resources")]
        public List<ResourceData> Resources { get; set; }

        [JsonProperty("owner")]
        public bool Owner { get; set; }

        [JsonProperty("last_play_date")]
        public long LastPlayDate { get; set; }

        [JsonProperty("play_time")]
        public long PlayTime { get; set; }

        [JsonProperty("release_created_at")]
        public long ReleaseCreatedAt { get; set; }

        [JsonProperty("release_modified_at")]
        public long ReleaseModifiedAt { get; set; }

        [JsonProperty("is_early_access")]
        public bool IsEarlyAccess { get; set; }

        [JsonProperty("has_my_cart")]
        public bool HasMyCart { get; set; }

        [JsonProperty("has_my_wish_list")]
        public bool HasMyWishList { get; set; }

        [JsonProperty("has_ownership")]
        public bool HasOwnership { get; set; }

        [JsonProperty("product_detail_type")]
        public string ProductDetailType { get; set; }

        [JsonProperty("demo")]
        public bool Demo { get; set; }

        [JsonProperty("paid")]
        public bool Paid { get; set; }
    }
}