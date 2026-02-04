using System.ComponentModel.DataAnnotations.Schema;

namespace MarketPortfolioAnalytics.Models
{
    public class Bond : Asset
    {
        [Column("MaturityDate")]
        public DateTime? MaturityDate { get; set; }

        [Column("CouponRate")]
        public decimal? CouponRate { get; set; }
    }
}
