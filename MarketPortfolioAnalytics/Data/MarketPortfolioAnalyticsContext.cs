using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Data
{
    public class MarketPortfolioAnalyticsContext : DbContext
    {
        public MarketPortfolioAnalyticsContext (DbContextOptions<MarketPortfolioAnalyticsContext> options)
            : base(options)
        {
        }

        public DbSet<MarketPortfolioAnalytics.Models.AssetPrice> AssetPrice { get; set; } = default!;
        public DbSet<MarketPortfolioAnalytics.Models.Asset> Asset { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Position>()
                .HasKey(p => new { p.AssetId, p.PortfolioId })
                .HasName("PK_Position");
        }
        public DbSet<MarketPortfolioAnalytics.Models.Position> Position { get; set; } = default!;
        public DbSet<MarketPortfolioAnalytics.Models.Portfolio> Portfolio { get; set; } = default!;
        public DbSet<MarketPortfolioAnalytics.Models.AppUser> AppUser { get; set; } = default!;
    }
}
