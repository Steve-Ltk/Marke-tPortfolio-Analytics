using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;
using static Marke_tPortfolio_Analytics_web.Helpers.InsightHelper;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel de la page Analytics principale
    // Contient les paramètres du formulaire + les résultats de l'analyse
    public class AnalyticsIndexViewModel
    {
        // Liste des portefeuilles de l'user -> pour le menu déroulant
        public List<Portfolio> Portfolios { get; set; } = new();

        // Portefeuille sélectionné dans le formulaire
        public int SelectedPortfolioId { get; set; }
        public string SelectedPortfolioName { get; set; } = string.Empty;

        // Période d'analyse -> valeurs par défaut = 1 an glissant
        public DateTime DateDebut { get; set; } = DateTime.UtcNow.AddYears(-1);
        public DateTime DateFin { get; set; } = DateTime.UtcNow;
        public double TauxSansRisque { get; set; } = 4.5;

        // true si l'analyse a été lancée et a retourné un résultat
        public bool HasResult => Analyse != null;
        // Résultat de l'analyse -> null si pas encore analysé
        public PortfolioAnalyticsResult? Analyse { get; set; }

        // Insights qualitatifs générés depuis les métriques par InsightHelper
        // Null si HasResult = false
        public InsightResult? InsightSharpe { get; set; }
        public InsightResult? InsightVolatilite { get; set; }  // Volatility * 100
        public InsightResult? InsightDrawdown { get; set; }  // MaxDrawdown * 100
        public InsightResult? InsightCagr { get; set; }  // AnnualizedReturn * 100
        // Message global affiché en haut de la page Analytics
        public string InsightGlobal { get; set; } = string.Empty;
         // Niveau global = le pire niveau parmi les 4 métriques
        public string InsightNiveau { get; set; } = string.Empty;
    }
}
