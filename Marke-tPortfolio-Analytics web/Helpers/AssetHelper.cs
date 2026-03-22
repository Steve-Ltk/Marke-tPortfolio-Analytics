using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.Helpers
{
    // Utilitaires statiques pour les actifs — utilisés dans tous les controllers MVC frontend.
    // "static" → pas besoin d'instance, on appelle directement AssetHelper.IsUsd(asset).
    // Centralisé ici pour éviter de répéter la même logique partout
    public static class AssetHelper
    {
        // Retourne "Stock" ou "Bond" selon le vrai type C# de l'objet
        // Utilise le pattern matching "is" -> vérifie le type réel à l'exécution
        // Asset est polymorphique -> un objet peut être Stock ou Bond derrière une référence Asset
        // "switch expression" -> syntaxe moderne équivalente à if/else if/else
        public static string GetTypeLabel(Asset? asset) => asset switch
        {
            Bond => "Bond",
            Stock => "Stock",
            _ => "Stock"
        };

        // Priorité 1 : Currency renseigné -> on vérifie directement
        // StringComparison.OrdinalIgnoreCase -> "USD" = "usd" = "Usd"
        public static bool IsUsd(Asset? asset)
        {
            // Actif null -> on suppose USD par défaut (actifs US majoritaires)
            if (asset == null) return true;
            // Priorité 1 : Currency renseigné -> on vérifie directement
            // StringComparison.OrdinalIgnoreCase -> "USD" = "usd" = "Usd"
            if (!string.IsNullOrEmpty(asset.Currency))
                return string.Equals(asset.Currency, "USD",
                    StringComparison.OrdinalIgnoreCase);
            // Fallback : les tickers Euronext contiennent un point (.PA, .AS...)
            return !asset.Ticker.Contains('.');
        }
    }
}
