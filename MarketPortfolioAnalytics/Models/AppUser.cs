using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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

        [JsonIgnore]
        [ValidateNever]
        public string PasswordHash { get; set; } = string.Empty;


        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Password { get; set; }

        public DateTime? PasswordUpdatedAt { get; set; }

        public virtual ICollection<Portfolio>? ListePortfolios { get; set; }

    }
}
