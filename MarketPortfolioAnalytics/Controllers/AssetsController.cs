using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Services;

namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssetsController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context; // accès à la base
        private readonly FmpService _fmp; // accès à l'API FMP
        private readonly ILogger<AssetsController> _logger; // pour écrire des logs

        public AssetsController(
            MarketPortfolioAnalyticsContext context,
            FmpService fmp,
            ILogger<AssetsController> logger)
        {
            _context = context; // _context -> pour lire/écrire en base de données
            _fmp = fmp; // _fmp -> pour appeler l'API Financial Modeling Prep
            _logger = logger; // _logger -> pour écrire des messages de debug/erreur dans la console
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Asset>>> GetAll()
            => await _context.Asset.ToListAsync();
            
        // GetById cherche d'abord dans Stock, puis Bond, puis Asset générique.
        // Nécessaire à cause du TPT : FindAsync sur Asset retourne un objet de base
        // sans les propriétés spécifiques (Sector, CouponRate...).
        // _context.Set<Stock>() = accès à la table Stock sans DbSet déclaré.
        [HttpGet("{id}")]
        public async Task<ActionResult<Asset>> GetById(int id)
        {
            // Je cherche d'abord dans Stock
            var stock = await _context.Set<Stock>().FirstOrDefaultAsync(s => s.Id == id);
            if (stock is not null) return stock;

            // Puis dans Bond
            var bond = await _context.Set<Bond>().FirstOrDefaultAsync(b => b.Id == id);
            if (bond is not null) return bond;

            // Sinon Asset générique
            var asset = await _context.Asset.FindAsync(id);
            if (asset is null) return NotFound($"Actif {id} introuvable.");
            return asset;
        }

        [HttpGet("by-ticker/{ticker}")]
        public async Task<ActionResult<Asset>> GetByTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return BadRequest("Le ticker est requis.");

            string normalized = ticker.Trim().ToUpper();
            var asset = await _context.Asset.FirstOrDefaultAsync(a => a.Ticker == normalized);

            if (asset is null)
                return NotFound($"Aucun actif trouvé pour le ticker '{normalized}'.");

            return asset;
        }

        // Retourne le prix actuel d'un actif via FMP.
        // try/catch : si FMP tombe en panne, on retourne 503 proprement
        // au lieu de planter toute l'app avec une exception non gérée.
        // 503 = "service tiers indisponible" → différent d'une erreur de notre code.
        [HttpGet("price/{ticker}")]
        public async Task<ActionResult<decimal>> GetPrice(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return BadRequest("Le ticker est requis.");

            string normalized = ticker.Trim().ToUpper();

            try
            {
                var price = await _fmp.GetLatestPriceAsync(normalized);
                if (price is null)
                    return NotFound($"Prix introuvable pour '{normalized}'.");

                return Ok(price.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GetPrice {Ticker}", normalized);
                return StatusCode(503, "Service FMP temporairement indisponible.");
            }
        }

        // Retourne le prix ET la variation journalière en un seul appel FMP.
        // var (price, change) = -> déconstruction de tuple : deux valeurs en une ligne.
        // new { price, change } -> objet anonyme, pas besoin de créer une classe pour ça.
        // Si price == 0 -> FMP n'a pas répondu → 404.
        [HttpGet("quote/{ticker}")]
        public async Task<ActionResult<object>> GetQuote(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return BadRequest("Ticker requis.");

            var (price, change) = await _fmp.GetQuoteAsync(ticker.Trim().ToUpper());
            if (price == 0m) return NotFound();
            return Ok(new { price, change });
        }

        [HttpGet("exchange-rate")]
        public async Task<ActionResult<decimal>> GetExchangeRate(
            [FromQuery] string from = "EUR",
            [FromQuery] string to = "USD")
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return Ok(1.0m);

            try
            {
                decimal rate = await _fmp.GetExchangeRateAsync(from.ToUpper(), to.ToUpper());
                return Ok(rate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GetExchangeRate {From}/{To}", from, to);
                return Ok(1.0m);
            }
        }

         // Importe une action depuis FMP en 3 étapes :
         // 1. Vérifie que le ticker n'existe pas déjà en base
         // 2. Récupère le profil complet depuis FMP (nom, devise, exchange, secteur...)
         // 3. Crée le Stock en base avec ces données
         //
         // profile.Sector ?? input.Sector → prend FMP en priorité, sinon ce que le client envoie.
         // DeclaredType = typeof(Asset) → force la sérialisation polymorphique avec "assetType".
        [HttpPost("stocks/from-fmp")]
        public async Task<ActionResult<Asset>> CreateStock([FromBody] CreateStockRequest input)
        {
            if (string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Le ticker est requis.");

            string ticker = input.Ticker.Trim().ToUpper();

            if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
                return Conflict($"Un actif avec le ticker '{ticker}' existe déjà.");

            FmpProfile? profile = await _fmp.GetProfileAsync(ticker);

            if (profile is null)
                return BadRequest(
                    $"Ticker '{ticker}' introuvable sur FMP ou erreur de l'API externe. " +
                    "Vérifiez que le ticker est correct (ex: 'AAPL', 'MC.PA').");

            var stock = new Stock
            {
                Ticker = profile.Symbol,
                Name = profile.Name,
                Currency = profile.Currency,
                Exchange = profile.Exchange,
                CreatedAt = DateTime.UtcNow,
                Sector = !string.IsNullOrWhiteSpace(profile.Sector) ? profile.Sector.Trim() : input.Sector?.Trim(),
                ISIN = !string.IsNullOrWhiteSpace(profile.Isin) ? profile.Isin.Trim() : input.ISIN?.Trim()
            };

            _context.Asset.Add(stock);
            await _context.SaveChangesAsync();

            var result = CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
            result.DeclaredType = typeof(Asset);
            return result;
        }

        [HttpPost("bonds/from-fmp")]
        public async Task<ActionResult<Asset>> CreateBond([FromBody] JsonElement body)
        {
            if (!body.TryGetProperty("ticker", out var tickerEl)
                || string.IsNullOrWhiteSpace(tickerEl.GetString()))
                return BadRequest("Le ticker est requis.");

            string ticker = tickerEl.GetString()!.Trim().ToUpper();

            if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
                return Conflict($"Un actif avec le ticker '{ticker}' existe déjà.");

            FmpProfile? profile = await _fmp.GetProfileAsync(ticker);
            if (profile is null)
                return BadRequest($"Ticker '{ticker}' introuvable sur FMP.");

            FmpBondInfo? bondInfo = await _fmp.GetBondAsync(ticker);

            decimal? clientCoupon = null;
            if (body.TryGetProperty("couponRate", out var couponEl)
                && couponEl.ValueKind == JsonValueKind.Number)
                clientCoupon = couponEl.GetDecimal();

            DateTime? clientMaturity = null;
            if (body.TryGetProperty("maturityDate", out var maturityEl)
                && maturityEl.ValueKind == JsonValueKind.String
                && DateTime.TryParse(maturityEl.GetString(), out var parsedDate))
                clientMaturity = parsedDate.Date;

            var bond = new Bond
            {
                Ticker = profile.Symbol,
                Name = profile.Name,
                Currency = profile.Currency,
                Exchange = profile.Exchange,
                CreatedAt = DateTime.UtcNow,
                CouponRate = bondInfo?.CouponRate ?? clientCoupon,
                MaturityDate = bondInfo?.MaturityDate ?? clientMaturity
            };

            _context.Asset.Add(bond);
            await _context.SaveChangesAsync();

            var result = CreatedAtAction(nameof(GetById), new { id = bond.Id }, bond);
            result.DeclaredType = typeof(Asset);
            return result;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateAssetRequest input)
        {
            var asset = await _context.Asset.FindAsync(id);
            if (asset is null) return NotFound($"Actif {id} introuvable.");

            if (!string.IsNullOrWhiteSpace(input.Name))
                asset.Name = input.Name.Trim();

            if (input.Exchange is not null)
                asset.Exchange = string.IsNullOrWhiteSpace(input.Exchange) ? null : input.Exchange.Trim();

            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                string currency = input.Currency.Trim().ToUpper();
                if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                    return BadRequest("La devise doit être un code ISO 4217 à 3 lettres (ex: USD, EUR).");
                asset.Currency = currency;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Supprime un actif uniquement s'il n'est dans aucun portefeuille.
        // On vérifie en C# AVANT d'essayer en base -> message d'erreur clair pour le client.
        // Sans cette vérification, SQL Server lancerait une exception Restrict illisible.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var asset = await _context.Asset.FindAsync(id);
            if (asset is null) return NotFound($"Actif {id} introuvable.");

            bool used = await _context.Position.AnyAsync(p => p.AssetId == id);
            if (used)
                return BadRequest(
                    $"Impossible de supprimer '{asset.Ticker}' : utilisé dans un portefeuille. " +
                    "Supprimez d'abord les positions concernées.");

            _context.Asset.Remove(asset);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public record CreateStockRequest(string? Ticker, string? Sector, string? ISIN);
    public record UpdateAssetRequest(string? Name, string? Exchange, string? Currency);
}
