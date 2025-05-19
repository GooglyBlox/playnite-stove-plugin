using System;

namespace StoveLibrary.Models
{
    public class StoveGameData
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public string Genre { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string ImageUrl { get; set; }
        public string StoreUrl { get; set; }
        public string Description { get; set; }
        public string[] Tags { get; set; }
    }
}