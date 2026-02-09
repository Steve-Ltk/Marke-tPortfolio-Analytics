using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models.Requests
{
    public class AssetFromFmpRequest
    {
        [Required]
        public string Ticker { get; set; } = null!;
    }
}
