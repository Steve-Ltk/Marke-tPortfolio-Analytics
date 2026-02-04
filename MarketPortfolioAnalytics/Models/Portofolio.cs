using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models
{
    [Table("Portfolio")]
    public class Portfolio
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column("Name")]
        public string Name { get; set; } = null!;

        [Required]
        [Column("Currency")]
        public string Currency { get; set; } = "EUR";

        [Required]
        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("AppUsers")]
        public int UserId { get; set; }

        public virtual AppUser? User { get; set; }

        public virtual ICollection<Position>? ListePositions { get; set; }
    }
}