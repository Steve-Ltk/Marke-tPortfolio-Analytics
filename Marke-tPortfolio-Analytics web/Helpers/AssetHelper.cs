using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.Helpers
{
    /// <summary>
    /// Utilitaires pour les actifs.
    /// Asset.AssetType n'est PAS une propriété C# — c'est un discriminateur JSON.
    /// On utilise le pattern matching is Stock / is Bond pour identifier le type réel.
    /// </summary>
    public static class AssetHelper
    {
        /// <summary>Retourne "Stock" ou "Bond" selon le type C# réel de l'objet.</summary>
        public static string GetTypeLabel(Asset? asset) => asset switch
        {
            Bond => "Bond",
            Stock => "Stock",
            _ => "Stock"
        };

        /// <summary>
        /// Retourne true si l'actif est coté en USD.
        /// Priorité au champ Currency, sinon heuristique sur le ticker.
        /// </summary>
        public static bool IsUsd(Asset? asset)
        {
            if (asset == null) return true;
            if (!string.IsNullOrEmpty(asset.Currency))
                return string.Equals(asset.Currency, "USD",
                    StringComparison.OrdinalIgnoreCase);
            // Fallback : les tickers Euronext contiennent un point (.PA, .AS...)
            return !asset.Ticker.Contains('.');
        }
    }
}