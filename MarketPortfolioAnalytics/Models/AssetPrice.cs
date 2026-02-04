using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models
{
    [Table("AssetPrice")]
    public class AssetPrice
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Asset")]
        public int AssetId { get; set; }
        public virtual Asset? Asset { get; set; }

        [Required]
        [Column("Date")]
        public DateTime Date { get; set; }

        [Column("Open")]
        public decimal? Open { get; set; }

        [Column("High")]
        public decimal? High { get; set; }

        [Column("Low")]
        public decimal? Low { get; set; }

        [Required]
        [Column("Close")]
        public decimal Close { get; set; }

        [Column("Volume")]
        public long? Volume { get; set; }
    }
}
