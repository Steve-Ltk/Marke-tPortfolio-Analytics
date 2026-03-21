namespace MarketPortfolioAnalytics.Services
{
    // Paramètres de connexion à l'API FMP (Financial Modeling Prep).
    // Lus depuis appsettings.json, section "Fmp".
    // appsettings.json :
    // {
    //   "Fmp": {
    //     "BaseUrl": "https://financialmodelingprep.com",
    //     "ApiKey":  "Notre_clé_ici"
    //   }
    // }
    public class FmpOptions
    {
        public string BaseUrl { get; set; } = null!;
        public string ApiKey { get; set; } = null!;
    }
}
