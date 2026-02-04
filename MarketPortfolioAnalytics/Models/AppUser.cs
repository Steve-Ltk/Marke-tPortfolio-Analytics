using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Models
{
    [Table("AppUser")]
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column("FullName")]
        public string FullName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [Column("Email")]
        public string Email { get; set; } = null!;

        [Required]
        [Column("Role")]
        public string Role { get; set; } = "User"; 

        [Required]
        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Required]
        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<Portfolio>? ListePortfolios { get; set; }

    }
}
