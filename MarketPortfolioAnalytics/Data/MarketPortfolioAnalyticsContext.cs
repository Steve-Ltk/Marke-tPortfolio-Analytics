using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Data
{
    /// <summary>
    /// Point d'entrée d'Entity Framework Core vers la base SQL Server.
    /// Configure les tables, les relations, les contraintes et les comportements de suppression.
    /// </summary>
    public class MarketPortfolioAnalyticsContext : DbContext
    {
        public MarketPortfolioAnalyticsContext(DbContextOptions<MarketPortfolioAnalyticsContext> options)
            : base(options) { }

        // ── DbSets ────────────────────────────────────────────────────────────
        // Un DbSet par entité principale.
        // Stock et Bond n'ont pas besoin de DbSet séparé :
        // EF les gère via DbSet<Asset> grâce à la configuration TPT ci-dessous.

        public DbSet<AppUser> AppUser { get; set; } = default!;
        public DbSet<Portfolio> Portfolio { get; set; } = default!;
        public DbSet<Asset> Asset { get; set; } = default!;
        public DbSet<AssetPrice> AssetPrice { get; set; } = default!;
        public DbSet<Position> Position { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Héritage TPT (Table Per Type) ─────────────────────────────────
            //
            // EF crée 3 tables :
            //   "Asset"  → colonnes communes (Id, Name, Ticker, Exchange, Currency, CreatedAt)
            //   "Stock"  → uniquement (Id FK, Sector, ISIN)
            //   "Bond"   → uniquement (Id FK, MaturityDate, CouponRate)
            //
            // Quand on charge un Stock, EF fait une jointure Asset ↔ Stock automatiquement.

            modelBuilder.Entity<Asset>().ToTable("Asset");
            modelBuilder.Entity<Stock>().ToTable("Stock");
            modelBuilder.Entity<Bond>().ToTable("Bond");

            // ── Index d'unicité ───────────────────────────────────────────────

            // Un email ne peut appartenir qu'à un seul utilisateur
            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasDatabaseName("IX_AppUser_Email");

            // Un ticker est unique dans toute la table Asset
            // → on ne peut pas avoir deux fois "AAPL"
            modelBuilder.Entity<Asset>()
                .HasIndex(a => a.Ticker)
                .IsUnique()
                .HasDatabaseName("IX_Asset_Ticker");

            // Un seul prix par actif par date
            // → empêche les doublons lors des synchronisations FMP
            modelBuilder.Entity<AssetPrice>()
                .HasIndex(ap => new { ap.AssetId, ap.Date })
                .IsUnique()
                .HasDatabaseName("IX_AssetPrice_AssetId_Date");

            // ── Clé composite Position (PortfolioId, AssetId) ─────────────────
            //
            // Un actif ne peut apparaître qu'une seule fois dans un portefeuille.
            // EF ne peut pas deviner cette PK composite automatiquement
            // → on la déclare explicitement ici.

            modelBuilder.Entity<Position>()
                .HasKey(p => new { p.PortfolioId, p.AssetId });

            // ── Relations et comportements de suppression ─────────────────────

            // AppUser → Portfolio
            // Restrict : on ne peut pas supprimer un AppUser qui possède des portefeuilles.
            // En pratique : on désactive l'utilisateur (soft delete), on ne le supprime pas.
            modelBuilder.Entity<Portfolio>()
                .HasOne(p => p.User)
                .WithMany(u => u.ListePortfolios)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Portfolio → Position
            // Cascade : supprimer un portefeuille supprime toutes ses positions.
            // Le contrôleur bloque aussi la suppression si des positions existent
            // (double sécurité applicative + base).
            modelBuilder.Entity<Position>()
                .HasOne(p => p.Portfolio)
                .WithMany(pf => pf.ListePositions)
                .HasForeignKey(p => p.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Asset → Position
            // Restrict : on ne peut pas supprimer un actif utilisé dans une position.
            // Le contrôleur vérifie aussi cela avant de tenter la suppression.
            modelBuilder.Entity<Position>()
                .HasOne(p => p.Asset)
                .WithMany()
                .HasForeignKey(p => p.AssetId)
                .OnDelete(DeleteBehavior.Restrict);

            // Asset → AssetPrice
            // Cascade : supprimer un actif supprime tout son historique de prix.
            // Logique : sans l'actif, les prix n'ont plus de sens.
            modelBuilder.Entity<AssetPrice>()
                .HasOne(ap => ap.Asset)
                .WithMany(a => a.Prices)
                .HasForeignKey(ap => ap.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
