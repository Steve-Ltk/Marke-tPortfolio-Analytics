using Marke_tPortfolio_Analytics_web.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC avec vues Razor -> AddControllersWithViews (pas AddControllers qui retourne JSON seulement)
// ConfigureApplicationPartManager -> empêche ASP.NET de charger les controllers du backend
// Le frontend référence le projet backend pour partager les modèles (Asset, Portfolio...)
// Sans ce bloc -> les controllers backend seraient enregistrés en double -> conflit de routes
builder.Services.AddControllersWithViews()
    .ConfigureApplicationPartManager(apm => 
    { 
        var toRemove = apm.ApplicationParts
        .Where(ap => ap.Name == "MarketPortfolioAnalytics")
        .ToList(); 
        foreach (var ap in toRemove) 
            apm.ApplicationParts.Remove(ap); 
    });

// Session -> stocke UserId, UserName, UserEmail côté serveur
// DistributedMemoryCache -> cache en mémoire (dev uniquement)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(120); // session expire après 2h d'inactivité
    options.Cookie.HttpOnly = true;   // cookie inaccessible depuis JavaScript -> sécurité XSS
    options.Cookie.IsEssential = true;   // pas bloqué par le consentement RGPD
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
});

// HttpClient nommé "ApiClient" -> utilisé par ApiService via IHttpClientFactory
// BaseUrl lue depuis appsettings.json -> "http://localhost:5154"
// Timeout 30s -> si le backend ne répond pas en 30s -> erreur retournée proprement
builder.Services.AddHttpClient("ApiClient", client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"]
                  ?? throw new InvalidOperationException(
                      "ApiSettings:BaseUrl manquant dans appsettings.json");
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ApiService -> service qui fait tous les appels HTTP vers le backend
// Scoped -> une instance par requête HTTP -> cohérent avec le cycle de vie des controllers
builder.Services.AddScoped<IApiService, ApiService>();

// Accès au HttpContext dans les services (ex : ApiService lit la session)
builder.Services.AddHttpContextAccessor();

// Logging console -> visible dans la console Visual Studio pendant le debug
builder.Logging.AddConsole();

var app = builder.Build();


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection(); // redirect HTTP -> HTTPS
app.UseStaticFiles(); // sert wwwroot/ (CSS, JS, images)
app.UseRouting(); // active le routing par attributs et conventions

// UseSession DOIT être avant UseAuthorization
// Si inversé -> la session n'est pas disponible pendant l'autorisation
app.UseSession();
app.UseAuthorization();

// Route racine / -> Auth/Login (pas Dashboard)
// L'user non connecté atterrit sur la connexion
app.MapControllerRoute(
    name: "login",
    pattern: "",
    defaults: new { controller = "Auth", action = "Login" });

// Route générale -> {controller}/{action}/{id?}
// action par défaut = Index si non spécifiée
app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

app.Run();


