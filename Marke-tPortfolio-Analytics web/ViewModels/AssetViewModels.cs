using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ════════════════════════════════════════════════════════════════════
    // INDEX — liste de tous les actifs
    // ════════════════════════════════════════════════════════════════════

    public class AssetIndexViewModel
    {
        public List<AssetCard> Assets { get; set; } = new();
        public decimal TauxEurUsd { get; set; } = 1m;
        public string Recherche { get; set; } = string.Empty;
        public string FiltreType { get; set; } = "Tous"; // "Tous" | "Stock" | "Bond"

        public List<AssetCard> AssetsFiltrés => Assets
            .Where(a =>
                (FiltreType == "Tous" || a.TypeLabel == FiltreType) &&
                (string.IsNullOrEmpty(Recherche) ||
                 a.Ticker.Contains(Recherche, StringComparison.OrdinalIgnoreCase) ||
                 a.Nom.Contains(Recherche, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => Math.Abs(a.VariationJour))
            .ToList();

        public int NbStocks => Assets.Count(a => a.TypeLabel == "Stock");
        public int NbBonds => Assets.Count(a => a.TypeLabel == "Bond");
    }

    public class AssetCard
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string TypeLabel { get; set; } = "Stock"; // "Stock" | "Bond"
        public string Exchange { get; set; } = string.Empty;
        public string DeviseNative { get; set; } = "USD";
        public decimal PrixNatif { get; set; }  // dans la devise native
        public decimal PrixEur { get; set; }  // converti EUR
        public decimal PrixUsd { get; set; }  // converti USD
        public decimal VariationJour { get; set; }  // % 24h

        // Couleurs heatmap selon variation
        public string HeatmapBg => VariationJour switch
        {
            > 3 => "rgba(0,208,132,.25)",
            > 1 => "rgba(0,208,132,.14)",
            > 0 => "rgba(0,208,132,.07)",
            > -1 => "rgba(244,63,94,.07)",
            > -3 => "rgba(244,63,94,.14)",
            _ => "rgba(244,63,94,.25)"
        };

        public string HeatmapBorder => VariationJour switch
        {
            > 3 => "rgba(0,208,132,.5)",
            > 1 => "rgba(0,208,132,.3)",
            > 0 => "rgba(0,208,132,.15)",
            > -1 => "rgba(244,63,94,.15)",
            > -3 => "rgba(244,63,94,.3)",
            _ => "rgba(244,63,94,.5)"
        };

        public string VariationCouleur => VariationJour >= 0 ? "var(--green)" : "var(--red)";
        public string VariationSigne => VariationJour >= 0 ? "▲" : "▼";
    }

    // ════════════════════════════════════════════════════════════════════
    // DÉTAIL
    // ════════════════════════════════════════════════════════════════════

    public class AssetDetailsViewModel
    {
        public Asset Asset { get; set; } = null!;
        public string TypeLabel { get; set; } = "Stock";
        public decimal PrixActuel { get; set; }
        public decimal PrixEur { get; set; }
        public decimal PrixUsd { get; set; }
        public decimal VariationJour { get; set; }
        public decimal TauxEurUsd { get; set; }

        // Historique (30 derniers points pour le sparkline)
        public List<PrixHistorique> Historique { get; set; } = new();

        // Portefeuilles de l'user qui détiennent cet actif
        public List<string> PortefeuillesDetenant { get; set; } = new();
        public bool EstDansPortefeuille => PortefeuillesDetenant.Any();

        public string VariationCouleur => VariationJour >= 0 ? "var(--green)" : "var(--red)";
        public string VariationSigne => VariationJour >= 0 ? "+" : "";
    }

    public class PrixHistorique
    {
        public DateTime Date { get; set; }
        public decimal Cloture { get; set; }
    }
}
