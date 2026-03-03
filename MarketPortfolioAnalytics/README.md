# Market Portfolio Analytics — API ASP.NET Core 8

API REST de gestion et d'analyse de portefeuilles financiers.
Développée dans le cadre d'un projet Master.

**Étudiants :** GOUTHON Fallen, LANTONKPODE Sèmèvo

---

## Stack technique

| Couche         | Technologie                        |
|----------------|------------------------------------|
| Framework      | ASP.NET Core 8 (Web API)           |
| ORM            | Entity Framework Core 8            |
| Base de données | SQL Server                        |
| Héritage DB    | Table Per Type (TPT)               |
| Données externes | Financial Modeling Prep (FMP)    |
| Documentation  | Swagger / OpenAPI                  |
| Hashage mdp    | PBKDF2 (ASP.NET Identity)          |

---

## Architecture

```
MarketPortfolioAnalytics/
│
├── Controllers/
│   ├── AppUsersController.cs         # CRUD utilisateurs + soft delete + rôles
│   ├── AssetsController.cs           # CRUD actifs (Stock/Bond) + intégration FMP
│   ├── AssetPricesController.cs      # Prix historiques + sync FMP
│   ├── PortfoliosController.cs       # CRUD portefeuilles
│   ├── PositionsController.cs        # CRUD positions (clé composite)
│   └── AnalyticsController.cs        # Analyse, Markowitz, Monte Carlo, Backtest
│
├── Data/
│   └── MarketPortfolioAnalyticsContext.cs   # DbContext EF Core
│
├── Models/
│   ├── AppUser.cs                    # Utilisateur (PBKDF2, soft delete)
│   ├── Asset.cs                      # Asset + Stock + Bond (héritage TPT)
│   ├── AssetPrice.cs                 # Prix OHLCV journalier
│   ├── Portfolio.cs                  # Portefeuille
│   ├── Position.cs                   # Position (clé composite PortfolioId+AssetId)
│   └── Analytics/
│       └── AnalyticsModels.cs        # DTOs pour tous les endpoints analytiques
│
├── Services/
│   ├── FmpOptions.cs                 # Configuration FMP (BaseUrl, ApiKey)
│   ├── FmpService.cs                 # Client HTTP FMP (Profile, Prix, Obligations)
│   ├── FinancialMath.cs              # Bibliothèque de calculs financiers (statique)
│   ├── PortfolioAnalyticsService.cs  # Analyse de base (valeur, P&L, Sharpe, MDD...)
│   ├── PortfolioOptimizationService.cs # Markowitz par simulation Monte Carlo
│   ├── MonteCarloService.cs          # Simulation GBM de l'évolution future
│   └── BacktestService.cs            # Backtesting historique avec rééquilibrage
│
├── Program.cs                        # Point d'entrée + DI + pipeline HTTP
├── appsettings.json                  # Configuration (connection string, FMP)
├── appsettings.Development.json      # Surcharges dev (logs SQL)
└── MarketPortfolioAnalytics.csproj   # Dépendances NuGet
```

---

## Démarrage rapide

### 1. Configurer `appsettings.json`

```json
{
  "ConnectionStrings": {
    "MarketPortfolioAnalyticsContext": "Server=localhost;Database=MarketPortfolioAnalytics;Trusted_Connection=True;"
  },
  "Fmp": {
    "BaseUrl": "https://financialmodelingprep.com",
    "ApiKey":  "VOTRE_CLE_FMP_ICI"
  }
}
```

### 2. Lancer l'API

```bash
dotnet run
```

La base de données est créée automatiquement au démarrage (`EnsureCreated`).
Swagger disponible sur : `http://localhost:5154/swagger`

---

## Endpoints principaux

### Utilisateurs
| Méthode | Route | Description |
|---------|-------|-------------|
| GET | `/api/AppUsers` | Liste des utilisateurs actifs |
| POST | `/api/AppUsers` | Créer un utilisateur |
| PUT | `/api/AppUsers/{id}` | Modifier nom/email |
| PATCH | `/api/AppUsers/{id}/role` | Changer le rôle |
| PATCH | `/api/AppUsers/{id}/password` | Changer le mot de passe |
| DELETE | `/api/AppUsers/{id}` | Soft delete |
| PATCH | `/api/AppUsers/{id}/activate` | Réactiver |

### Actifs
| Méthode | Route | Description |
|---------|-------|-------------|
| GET | `/api/Assets` | Liste des actifs |
| POST | `/api/Assets/stocks/from-fmp` | Créer une action depuis FMP |
| POST | `/api/Assets/bonds/from-fmp` | Créer une obligation depuis FMP |
| GET | `/api/Assets/by-ticker/{ticker}` | Recherche par ticker |

### Prix
| Méthode | Route | Description |
|---------|-------|-------------|
| GET | `/api/AssetPrices/by-asset/{id}` | Historique avec filtres |
| POST | `/api/AssetPrices/sync/{id}` | Synchroniser depuis FMP |
| POST | `/api/AssetPrices` | Ajout manuel |

### Portefeuilles & Positions
| Méthode | Route | Description |
|---------|-------|-------------|
| GET | `/api/Portfolios?userId={id}` | Portefeuilles d'un utilisateur |
| GET | `/api/Portfolios/{id}/details` | Détail avec positions |
| POST | `/api/Positions` | Ajouter une position |
| PUT | `/api/Positions/{portfolioId}/{assetId}` | Modifier une position |

### Analytics
| Méthode | Route | Description |
|---------|-------|-------------|
| GET | `/api/Analytics/portfolios/{id}/analyze` | Analyse complète |
| POST | `/api/Analytics/portfolios/compare` | Comparaison multi-portefeuilles |
| POST | `/api/Analytics/portfolios/{id}/optimize` | Optimisation Markowitz |
| POST | `/api/Analytics/portfolios/{id}/montecarlo` | Simulation Monte Carlo |
| POST | `/api/Analytics/portfolios/{id}/backtest` | Backtesting historique |

---

## Formules financières implémentées

| Métrique | Formule |
|---|---|
| Rendement annualisé | `(1 + R_cumulé)^(252/n) − 1` |
| Volatilité annualisée | `σ_daily × √252` |
| Sharpe | `(R_p − R_f) / σ_p` |
| Sortino | `(R_p − R_f) / σ_downside` |
| Calmar | `R_annualisé / |MaxDrawdown|` |
| VaR historique | `Percentile(rendements, 1 − conf)` |
| CVaR / Expected Shortfall | `Moyenne des rendements ≤ VaR` |
| Bêta | `Cov(R_p, R_b) / Var(R_b)` |
| Alpha (Jensen) | `R_p − [R_f + β × (R_b − R_f)]` |
| GBM (Monte Carlo) | `V(t+1) = V(t) × exp((μ − σ²/2) + σZ)` |
| Markowitz | `σ_p = √(wᵀ Σ w)` |

---

## Corrections appliquées après audit (v2)

| Fichier | Bug | Correction |
|---|---|---|
| `FinancialMath.cs` | `VaR` : index `floor((1-conf)×n)` décalé d'un rang | Réutilise `Percentile()` — cohérent |
| `MonteCarloService.cs` | `using Models` manquant → erreur compile | `using` ajouté |
| `BacktestService.cs` | `GetLastPrice` O(n²) dans double boucle | Curseurs O(n) comme `PortfolioAnalyticsService` |
| `AssetPricesController.cs` | `ToHashSetAsync()` inexistant en EF Core | `ToListAsync().ToHashSet()` |
| `AppUsersController.cs` | `FullName` vide accepté silencieusement | Validation ajoutée |
| `AnalyticsController.cs` | `from`/`to` non validés contre `DateTime.MinValue` | Guard `== default` ajouté |
| `Program.cs` | `AddScoped<FmpService>()` → crash DI | `AddHttpClient<FmpService>()` |
| `Program.cs` | 4 services analytics commentés | Services enregistrés |
