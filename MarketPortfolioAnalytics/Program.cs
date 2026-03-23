using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Services;


var builder = WebApplication.CreateBuilder(args);

// Enregistre le DbContext avec la chaîne de connexion SQL Server.
// ?? throw = si la chaîne est absente d'appsettings.json, l'app refuse de démarrer.
// Mieux vaut planter au démarrage qu'à la première requête.

builder.Services.AddDbContext<MarketPortfolioAnalyticsContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MarketPortfolioAnalyticsContext")
        ?? throw new InvalidOperationException(
            "La chaîne de connexion 'MarketPortfolioAnalyticsContext' est introuvable. " +
            "Vérifiez appsettings.json.")));

// Lie la section "Fmp" d'appsettings.json à la classe FmpOptions.
// FmpService reçoit ces valeurs via IOptions<FmpOptions> dans son constructeur.
builder.Services.Configure<FmpOptions>(
    builder.Configuration.GetSection("Fmp"));

// AddHttpClient enregistre FmpService ET crée son HttpClient en même temps.
// Si on utilisait AddScoped seul -> crash au démarrage.
builder.Services.AddHttpClient<FmpService>();

// AddScoped = une instance par requête HTTP.
// Créé au début de la requête, détruit à la fin.
// Adapté aux services qui utilisent le DbContext (lui aussi est Scoped).
builder.Services.AddScoped<PortfolioAnalyticsService>();
builder.Services.AddScoped<PortfolioOptimizationService>();
builder.Services.AddScoped<MonteCarloService>();
builder.Services.AddScoped<BacktestService>();

// Enregistre tous les controllers de l'API.
// Sans ça, aucune route n'est reconnue → tout retourne 404.
// Gère aussi la sérialisation JSON automatique et la validation des modèles.
builder.Services.AddControllers();


builder.Services.AddEndpointsApiExplorer(); // <- "utilise le générateur Swagger du conteneur"
builder.Services.AddSwaggerGen(); // "utilise la politique CORS du conteneur"

// Pipeline = chaîne de montage que chaque requête HTTP traverse dans l'ordre.
// L'ordre est obligatoire — ne pas changer.
// Swagger uniquement en Development → pas exposé en production.
// MapControllers = connecte les URLs aux méthodes des controllers.

var app = builder.Build();

// Swagger — uniquement en développement 
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

// Nécessaire même sans authentification configurée (ASP.NET l'exige dans le pipeline)
app.UseAuthorization();

// Routage vers les contrôleurs
app.MapControllers();

//  Initialisation de la base de données 
// CreateScope = crée un contexte d'exécution artificiel.
// Nécessaire car on est au démarrage, hors requête HTTP.
// Sans ça, impossible de récupérer un service Scoped comme DbContext.
// Le "using" garantit que tout est détruit proprement à la fin du bloc.
using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    var context = scope.ServiceProvider
        .GetRequiredService<MarketPortfolioAnalyticsContext>();

    context.Database.EnsureDeleted();
    context.Database.EnsureCreated();

}


app.Run();
