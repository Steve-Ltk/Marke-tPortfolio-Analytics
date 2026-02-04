using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models
{
        [Table("Asset")]
        public class Asset
        {
            [Key]
            public int Id { get; set; }

            [Required]
            [Column("Name")]
            public string Name { get; set; } = null!;

            [Required]
            [Column("Ticker")]
            public string Ticker { get; set; } = null!; 

            [Column("Exchange")]
            public string? Exchange { get; set; }

            [Required]
            [Column("Currency")]
            public string Currency { get; set; } = null!;

            [Required]
            [Column("CreatedAt")]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public virtual ICollection<AssetPrice>? Prices { get; set; }
        }
}
