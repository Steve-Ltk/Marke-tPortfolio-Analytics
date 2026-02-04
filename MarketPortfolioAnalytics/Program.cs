using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MarketPortfolioAnalytics.Data;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<MarketPortfolioAnalyticsContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MarketPortfolioAnalyticsContext") ?? throw new InvalidOperationException("Connection string 'MarketPortfolioAnalyticsContext' not found.")));

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

using (var serviceScope = app.Services.GetService<IServiceScopeFactory>().CreateScope())
{
    var context = serviceScope.ServiceProvider.GetRequiredService<MarketPortfolioAnalyticsContext>();
    context.Database.EnsureDeleted();
    context.Database.EnsureCreated();
}

app.Run();
