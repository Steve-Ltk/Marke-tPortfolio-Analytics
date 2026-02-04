using System.ComponentModel.DataAnnotations.Schema;

namespace MarketPortfolioAnalytics.Models
{
    public class Stock : Asset
    {
        [Column("Sector")]
        public string? Sector { get; set; }

        [Column("ISIN")]
        public string? ISIN { get; set; }
    }
}

