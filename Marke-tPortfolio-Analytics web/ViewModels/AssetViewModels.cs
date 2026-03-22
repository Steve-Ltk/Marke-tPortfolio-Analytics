using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel de la liste des actifs (heatmap + tableau)
    public class AssetIndexViewModel
    {
        public List<AssetCard> Assets { get; set; } = new();
        public decimal TauxEurUsd { get; set; } = 1m;
        public string Recherche { get; set; } = string.Empty;
        public string FiltreType { get; set; } = "Tous"; // "Tous" | "Stock" | "Bond"

        // Propriété calculée -> filtre + tri par variation journalière absolue
        // Les actifs les plus mouvementés apparaissent en premier dans la heatmap
        public List<AssetCard> AssetsFiltrés => Assets
            .Where(a =>
                (FiltreType == "Tous" || a.TypeLabel == FiltreType) &&
                (string.IsNullOrEmpty(Recherche) ||
                 a.Ticker.Contains(Recherche, StringComparison.OrdinalIgnoreCase) ||
                 a.Nom.Contains(Recherche, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => Math.Abs(a.VariationJour))
            .ToList();

        // Compteurs pour l'en-tête de la page
        public int NbStocks => Assets.Count(a => a.TypeLabel == "Stock");
        public int NbBonds => Assets.Count(a => a.TypeLabel == "Bond");
    }

    // Une carte actif pour la heatmap et le tableau
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

        // Couleur de fond de la carte heatmap selon la variation
        // Plus la variation est forte, plus la couleur est intense
        public string HeatmapBg => VariationJour switch
        {
            > 3 => "rgba(0,208,132,.25)", // forte hausse -> vert intense
            > 1 => "rgba(0,208,132,.14)", // hausse modérée -> vert moyen
            > 0 => "rgba(0,208,132,.07)", // légère hausse -> vert pâle
            > -1 => "rgba(244,63,94,.07)", // légère baisse -> rouge pâle
            > -3 => "rgba(244,63,94,.14)", // baisse modérée -> rouge moyen
            _ => "rgba(244,63,94,.25)" // forte baisse -> rouge intense
        };

        // Couleur de bordure de la carte -> même logique que HeatmapBg
        public string HeatmapBorder => VariationJour switch
        {
            > 3 => "rgba(0,208,132,.5)",
            > 1 => "rgba(0,208,132,.3)",
            > 0 => "rgba(0,208,132,.15)",
            > -1 => "rgba(244,63,94,.15)",
            > -3 => "rgba(244,63,94,.3)",
            _ => "rgba(244,63,94,.5)"
        };

        // Couleur du texte de variation -> vert si hausse, rouge si baisse
        public string VariationCouleur => VariationJour >= 0 ? "var(--green)" : "var(--red)";
        // Flèche devant la variation -> ▲ si hausse, ▼ si baisse
        public string VariationSigne => VariationJour >= 0 ? "▲" : "▼";
    }

    // ViewModel de la page de détail d'un actif
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
        // Historique de prix pour le sparkline (optionnel, non utilisé actuellement)
        public List<PrixHistorique> Historique { get; set; } = new();

        // Portefeuilles de l'user qui détiennent cet actif
        public List<string> PortefeuillesDetenant { get; set; } = new();

        // true si l'actif est dans au moins un portefeuille de l'user
        // -> si true : bouton "Supprimer" masqué, bouton "Voir portefeuilles" affiché
        public bool EstDansPortefeuille => PortefeuillesDetenant.Any();

        public string VariationCouleur => VariationJour >= 0 ? "var(--green)" : "var(--red)";
        public string VariationSigne => VariationJour >= 0 ? "+" : "";
    }
     // Un point de l'historique de prix pour le sparkline
    public class PrixHistorique
    {
        public DateTime Date { get; set; }
        public decimal Cloture { get; set; }
    }
}
