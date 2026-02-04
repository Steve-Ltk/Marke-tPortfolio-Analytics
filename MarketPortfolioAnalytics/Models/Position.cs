using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models
{
    [Table("Position")]
    public class Position
    {
        // PK composite configurée en Fluent : (PortfolioId, AssetId)

        [ForeignKey("Portfolio")]
        public int PortfolioId { get; set; }
        public virtual Portfolio? Portfolio { get; set; }

        [ForeignKey("Asset")]
        public int AssetId { get; set; }
        public virtual Asset? Asset { get; set; }

        [Required]
        [Column("Quantity")]
        public decimal Quantity { get; set; }

        [Required]
        [Column("AvgBuyPrice")]
        public decimal AvgBuyPrice { get; set; }

        [Required]
        [Column("BuyDate")]
        public DateTime BuyDate { get; set; }

        [Required]
        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    }
}
