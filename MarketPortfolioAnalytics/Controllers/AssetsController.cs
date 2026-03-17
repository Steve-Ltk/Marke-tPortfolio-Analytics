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
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly FmpService _fmp;
        // ✅ Logger ajouté — nécessaire pour GetPrice et GetExchangeRate
        private readonly ILogger<AssetsController> _logger;

        public AssetsController(
            MarketPortfolioAnalyticsContext context,
            FmpService fmp,
            ILogger<AssetsController> logger)
        {
            _context = context;
            _fmp = fmp;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Asset>>> GetAll()
            => await _context.Asset.ToListAsync();

        [HttpGet("{id}")]
        public async Task<ActionResult<Asset>> GetById(int id)
        {
            var stock = await _context.Set<Stock>().FirstOrDefaultAsync(s => s.Id == id);
            if (stock is not null) return stock;

            var bond = await _context.Set<Bond>().FirstOrDefaultAsync(b => b.Id == id);
            if (bond is not null) return bond;

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

        // ═══════════════════════════════════════════════════════════════════════
        // PRIX TEMPS RÉEL
        // GET /api/Assets/price/{ticker}
        // ✅ Utilise _fmp.GetLatestPriceAsync() — méthode maintenant définie dans FmpService
        // ✅ Utilise _logger injecté dans le constructeur
        // ═══════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════
        // TAUX DE CHANGE
        // GET /api/Assets/exchange-rate?from=EUR&to=USD
        // ✅ Utilise _fmp.GetExchangeRateAsync() — méthode maintenant définie dans FmpService
        // ✅ Utilise _logger injecté dans le constructeur
        // ═══════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════
        // CRÉATION
        // ═══════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION
        // ═══════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════════
    // DTOs
    // ═══════════════════════════════════════════════════════════════════════════

    public record CreateStockRequest(string? Ticker, string? Sector, string? ISIN);
    public record UpdateAssetRequest(string? Name, string? Exchange, string? Currency);
}