using Newtonsoft.Json;
using System.Collections.Generic;

namespace StoveLibrary.Models
{
    public class SessionResponse
    {
        [JsonProperty("value")]
        public SessionValue Value { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }
    }

    public class SessionValue
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("expire_in")]
        public int ExpireIn { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("member")]
        public Member Member { get; set; }
    }

    public class Member
    {
        [JsonProperty("country_cd")]
        public string CountryCd { get; set; }

        [JsonProperty("profile_img")]
        public string ProfileImg { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("member_no")]
        public long MemberNo { get; set; }

        [JsonProperty("nickname")]
        public string Nickname { get; set; }

        [JsonProperty("person_verify_yn")]
        public string PersonVerifyYn { get; set; }

        [JsonProperty("parent_verify_yn")]
        public string ParentVerifyYn { get; set; }
    }

    public class GamesResponse
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("value")]
        public GamesValue Value { get; set; }
    }

    public class GamesValue
    {
        [JsonProperty("content")]
        public List<StoveGameData> Content { get; set; }

        [JsonProperty("total_elements")]
        public int TotalElements { get; set; }

        [JsonProperty("total_pages")]
        public int TotalPages { get; set; }

        [JsonProperty("number")]
        public int Number { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("first")]
        public bool First { get; set; }

        [JsonProperty("last")]
        public bool Last { get; set; }
    }

    public class StoreResponse
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("value")]
        public StoreValue Value { get; set; }
    }

    public class StoreValue
    {
        [JsonProperty("components")]
        public List<StoreComponent> Components { get; set; }
    }

    public class StoreComponent
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("props")]
        public StoreGameDetails Props { get; set; }
    }

    public class StoreGameDetails
    {
        [JsonProperty("product_no")]
        public int ProductNo { get; set; }

        [JsonProperty("product_name")]
        public string ProductName { get; set; }

        [JsonProperty("short_piece")]
        public string ShortPiece { get; set; }

        [JsonProperty("title_image_square")]
        public string TitleImageSquare { get; set; }

        [JsonProperty("title_image_rectangle")]
        public string TitleImageRectangle { get; set; }

        [JsonProperty("genres")]
        public List<StoreTag> Genres { get; set; }

        [JsonProperty("tags")]
        public List<StoreTag> Tags { get; set; }
    }

    public class StoreTag
    {
        [JsonProperty("tag_no")]
        public int TagNo { get; set; }

        [JsonProperty("tag_type")]
        public string TagType { get; set; }

        [JsonProperty("tag_name")]
        public string TagName { get; set; }
    }

    public class ResourceData
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("sort")]
        public int Sort { get; set; }

        [JsonProperty("resource_id")]
        public string ResourceId { get; set; }

        [JsonProperty("data")]
        public ResourceDataValue Data { get; set; }
    }

    public class ResourceDataValue
    {
        [JsonProperty("link_cdn")]
        public string LinkCdn { get; set; }
    }
}