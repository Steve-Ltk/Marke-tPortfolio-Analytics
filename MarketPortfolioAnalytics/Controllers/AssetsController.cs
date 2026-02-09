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

        // POST: api/Assets/from-fmp
        // Crée un Asset uniquement si le ticker existe sur FMP
        [HttpPost("from-fmp")]
        public async Task<ActionResult<Asset>> CreateAssetFromFmp([FromBody] AssetFromFmpRequest input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Ticker is required.");

            var ticker = input.Ticker.Trim().ToUpper();

            bool exists = await _context.Asset.AnyAsync(a => a.Ticker == ticker);
            if (exists) return Conflict("Ticker already exists.");

            var quote = await _fmp.GetQuoteMinimalAsync(ticker);
            if (quote == null)
                return BadRequest("Ticker not found on FMP (or FMP error).");

            var asset = new Asset
            {
                Ticker = quote.Value.Symbol,
                Name = quote.Value.Name,
                Exchange = quote.Value.Exchange,
                Currency = "USD", // stable/quote ne renvoie pas currency dans ton exemple
                CreatedAt = DateTime.UtcNow
            };

            _context.Asset.Add(asset);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAsset), new { id = asset.Id }, asset);
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
