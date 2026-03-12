using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// CONSTRUCTION DE L'APPLICATION
// ═══════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ── Base de données — SQL Server ───────────────────────────────────────────────
// La chaîne de connexion est lue depuis appsettings.json (section ConnectionStrings)
// Elle est obligatoire — l'application ne démarre pas si elle est absente

builder.Services.AddDbContext<MarketPortfolioAnalyticsContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MarketPortfolioAnalyticsContext")
        ?? throw new InvalidOperationException(
            "La chaîne de connexion 'MarketPortfolioAnalyticsContext' est introuvable. " +
            "Vérifiez appsettings.json.")));

// ── Configuration FMP — lue depuis appsettings.json, section "Fmp" ────────────
// Injectée dans FmpService via IOptions<FmpOptions>
// Structure attendue dans appsettings.json :
// {
//   "Fmp": {
//     "BaseUrl": "https://financialmodelingprep.com",
//     "ApiKey":  "votre_clé_ici"
//   }
// }
builder.Services.Configure<FmpOptions>(
    builder.Configuration.GetSection("Fmp"));

// ── Services métier — injection de dépendances ─────────────────────────────────
// AddScoped : une instance par requête HTTP (cycle de vie adapté aux services EF)

// Service FMP — AddHttpClient<FmpService>() enregistre à la fois :
//   - un HttpClient typé injecté dans le constructeur de FmpService
//   - FmpService elle-même dans le conteneur DI
// C'est la méthode correcte quand un service dépend directement de HttpClient.
// AddScoped<FmpService>() seul ne fonctionnerait pas car ASP.NET ne saurait pas
// quel HttpClient injecter.
builder.Services.AddHttpClient<FmpService>();

// Services d'analyse financière
// PortfolioAnalyticsService est enregistré en premier car les 3 autres en dépendent
builder.Services.AddScoped<PortfolioAnalyticsService>();
builder.Services.AddScoped<PortfolioOptimizationService>();
builder.Services.AddScoped<MonteCarloService>();
builder.Services.AddScoped<BacktestService>();

// ── Contrôleurs ────────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── Swagger — documentation interactive de l'API ───────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title       = "Market Portfolio Analytics API",
        Version     = "v1",
        Description =
            "API de gestion et d'analyse de portefeuilles financiers.\n\n" +
            "Fonctionnalités :\n" +
            "  - Gestion des utilisateurs, portefeuilles, actifs et positions\n" +
            "  - Synchronisation des prix historiques depuis FMP\n" +
            "  - Analyse financière : rendement, volatilité, Sharpe, drawdown\n" +
            "  - Optimisation Markowitz, simulation Monte Carlo, backtesting"
    });
});

// ── CORS — Cross-Origin Resource Sharing ──────────────────────────────────────
// Nécessaire si un frontend (React, Angular...) appelle l'API depuis un autre port
// En développement : on autorise tout (any origin, any method, any header)
// En production : remplacer AllowAnyOrigin() par WithOrigins("https://votre-domaine.com")
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentPolicy", policy =>
        policy
            .AllowAnyOrigin()   // à restreindre en production
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// ═══════════════════════════════════════════════════════════════════════════════
// CONFIGURATION DU PIPELINE HTTP
// ═══════════════════════════════════════════════════════════════════════════════

var app = builder.Build();

// ── Swagger — uniquement en développement ─────────────────────────────────────
// En production, Swagger est désactivé pour ne pas exposer la documentation
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Market Portfolio Analytics v1");
        options.RoutePrefix = "swagger";   // accessible sur http://localhost:5154/swagger
    });

    // CORS actif uniquement en développement
    app.UseCors("DevelopmentPolicy");
}

// ── Autorisation ───────────────────────────────────────────────────────────────
// Nécessaire même sans authentification configurée (ASP.NET l'exige dans le pipeline)
app.UseAuthorization();

// ── Routage vers les contrôleurs ───────────────────────────────────────────────
app.MapControllers();

// ── Initialisation de la base de données ──────────────────────────────────────
// EnsureCreated : crée les tables si la base n'existe pas encore.
// Ne modifie RIEN si la base existe déjà.
//
// ⚠️ IMPORTANT — ce qu'on n'utilise PAS ici et pourquoi :
//   - EnsureDeleted() : supprime et recrée la base à chaque démarrage → interdit en production
//   - Database.Migrate() : à utiliser à la place si on passe aux migrations EF
//
// Pour l'instant EnsureCreated est suffisant pour le projet académique.
// Quand vous passerez en production, remplacez par : context.Database.Migrate()
// et créez des migrations avec : dotnet ef migrations add InitialCreate

using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    var context = scope.ServiceProvider
        .GetRequiredService<MarketPortfolioAnalyticsContext>();

    //context.Database.EnsureDeleted();
    context.Database.EnsureCreated();

}

// ── Démarrage ──────────────────────────────────────────────────────────────────
app.Run();
