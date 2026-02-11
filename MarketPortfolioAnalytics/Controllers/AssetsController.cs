using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Services;
using MarketPortfolioAnalytics.Models.Requests;
using System.Text.Json;



namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssetsController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        private readonly FmpService _fmp;

        public AssetsController(MarketPortfolioAnalyticsContext context, FmpService fmp)
        {
            _context = context;
            _fmp = fmp;
        }


        // GET: api/Assets
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Asset>>> GetAssets()
        {
            return await _context.Asset.ToListAsync();
        }

        // GET: api/Assets/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Asset>> GetAsset(int id)
        {
            // 1) Stock ?
            var stock = await _context.Set<Stock>().FirstOrDefaultAsync(s => s.Id == id);
            if (stock != null) return stock;

            // 2) Bond ?
            var bond = await _context.Set<Bond>().FirstOrDefaultAsync(b => b.Id == id);
            if (bond != null) return bond;

            // 3) Asset simple ?
            var asset = await _context.Asset.FindAsync(id);
            if (asset == null) return NotFound();

            return asset;
        }


        // GET: api/Assets/by-ticker/AAPL
        [HttpGet("by-ticker/{ticker}")]
        public async Task<ActionResult<Asset>> GetByTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return BadRequest("Ticker is required.");

            var norm = ticker.Trim().ToUpper();

            var asset = await _context.Asset.FirstOrDefaultAsync(a => a.Ticker == norm);
            if (asset == null) return NotFound();

            return asset;
        }

        // POST: api/Assets
        // Création contrôlée : au minimum Ticker unique + normalisation
        [HttpPost]
        public async Task<ActionResult<Asset>> PostAsset(Asset input)
        {
            if (string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Ticker is required.");

            var ticker = input.Ticker.Trim().ToUpper();

            // Option: validation simple ticker (lettres/chiffres + . -)
            if (!Regex.IsMatch(ticker, @"^[A-Z0-9\.\-]{1,20}$"))
                return BadRequest("Ticker format is invalid.");

            bool exists = await _context.Asset.AnyAsync(a => a.Ticker == ticker);
            if (exists) return Conflict("Ticker already exists.");

            // MVP: on accepte Name/Currency/Exchange si tu les fournis,
            // mais idéalement ils viennent de FMP (on fera ensuite).
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Name is required (temporary MVP).");

            var currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpper();
            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return BadRequest("Currency must be a 3-letter code (e.g., EUR, USD).");

            var asset = new Asset
            {
                Ticker = ticker,
                Name = input.Name.Trim(),
                Exchange = string.IsNullOrWhiteSpace(input.Exchange) ? null : input.Exchange.Trim(),
                Currency = currency,
                CreatedAt = DateTime.UtcNow
            };

            _context.Asset.Add(asset);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAsset), new { id = asset.Id }, asset);
        }

        // POST: api/Assets/stocks/from-fmp
        // Body: { "ticker": "AAPL", "sector": "...", "isin": "..." } (sector/isin optionnels)
        [HttpPost("stocks/from-fmp")]
        public async Task<ActionResult<Asset>> CreateStockFromFmp([FromBody] Stock input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Ticker is required.");

            var ticker = input.Ticker.Trim().ToUpper();

            // Unicité sur Asset
            bool exists = await _context.Asset.AnyAsync(a => a.Ticker == ticker);
            if (exists) return Conflict("Ticker already exists.");

            // Validation + metadata depuis FMP
            var profile = await _fmp.GetProfileAsync(ticker);
            if (profile == null)
                return BadRequest("Ticker not found on FMP (or FMP error).");

            // Création Stock (TPT => écrit Asset + Stock)
            var stock = new Stock
            {
                Ticker = profile.Value.Symbol,
                Name = profile.Value.Name,
                Currency = profile.Value.Currency,
                Exchange = profile.Value.Exchange,
                CreatedAt = DateTime.UtcNow,

                // FMP -> sinon input -> sinon null
                Sector = !string.IsNullOrWhiteSpace(profile.Value.Sector)
                 ? profile.Value.Sector!.Trim()
                 : (string.IsNullOrWhiteSpace(input.Sector) ? null : input.Sector.Trim()),

                ISIN = !string.IsNullOrWhiteSpace(profile.Value.Isin)
                 ? profile.Value.Isin!.Trim()
                 : (string.IsNullOrWhiteSpace(input.ISIN) ? null : input.ISIN.Trim())
            };


            _context.Asset.Add(stock);   // important: Add sur le DbSet Asset marche (EF détecte Stock)
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAsset), new { id = stock.Id }, stock);
        }

        // POST: api/Assets/bonds/from-fmp
        // Body: { "ticker": "AAPL" }
    [HttpPost("bonds/from-fmp")]
    public async Task<ActionResult<Asset>> CreateBondFromFmp([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest("Body must be a JSON object.");

        if (!body.TryGetProperty("ticker", out var tEl) || string.IsNullOrWhiteSpace(tEl.GetString()))
            return BadRequest("Ticker is required.");

        var ticker = tEl.GetString()!.Trim().ToUpper();

        if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
            return Conflict("Ticker already exists.");

        var profile = await _fmp.GetProfileAsync(ticker);
        if (profile == null)
            return BadRequest("Ticker not found on FMP (or FMP error).");

        var bondInfo = await _fmp.GetBondAsync(ticker); // peut être null

        // input optionnel (override) : couponRate / maturityDate
        decimal? inputCoupon = null;
        if (body.TryGetProperty("couponRate", out var cEl) && cEl.ValueKind == JsonValueKind.Number)
            inputCoupon = cEl.GetDecimal();

        DateTime? inputMaturity = null;
        if (body.TryGetProperty("maturityDate", out var mEl) && mEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(mEl.GetString(), out var dt))
            inputMaturity = dt.Date;

        var bond = new Bond
        {
            Ticker = profile.Value.Symbol,
            Name = profile.Value.Name,
            Currency = profile.Value.Currency,
            Exchange = profile.Value.Exchange,
            CreatedAt = DateTime.UtcNow,

            // FMP -> sinon input -> sinon null
            CouponRate = bondInfo?.CouponRate ?? inputCoupon,
            MaturityDate = bondInfo?.MaturityDate ?? inputMaturity
        };

        _context.Asset.Add(bond);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAsset), new { id = bond.Id }, bond);
    }




    // PUT: api/Assets/5
    // Update contrôlé : on évite d'écraser tout
    [HttpPut("{id}")]
        public async Task<IActionResult> PutAsset(int id, Asset input)
        {
            var asset = await _context.Asset.FindAsync(id);
            if (asset == null) return NotFound();

            // On ne change pas Ticker ici (sinon problèmes de référence)
            if (!string.IsNullOrWhiteSpace(input.Name))
                asset.Name = input.Name.Trim();

            if (!string.IsNullOrWhiteSpace(input.Exchange))
                asset.Exchange = input.Exchange.Trim();

            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                var currency = input.Currency.Trim().ToUpper();
                if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                    return BadRequest("Currency must be a 3-letter code (e.g., EUR, USD).");

                asset.Currency = currency;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/Assets/5
        // (Option pro) refuser si l'asset est utilisé dans une position
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsset(int id)
        {
            var asset = await _context.Asset.FindAsync(id);
            if (asset == null) return NotFound();

            bool used = await _context.Position.AnyAsync(p => p.AssetId == id);
            if (used)
                return BadRequest("Cannot delete an asset used in positions.");

            _context.Asset.Remove(asset);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
