using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PositionsController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PositionsController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // GET: api/Positions
        // (Optionnel) liste globale — utile pour debug
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Position>>> GetPositions()
        {
            return await _context.Position
                .Include(p => p.Asset)
                .Include(p => p.Portfolio)
                .ToListAsync();
        }

        // GET: api/Positions/{portfolioId}/{assetId}
        [HttpGet("{portfolioId:int}/{assetId:int}")]
        public async Task<ActionResult<Position>> GetPosition(int portfolioId, int assetId)
        {
            var position = await _context.Position
                .Include(p => p.Asset)
                .FirstOrDefaultAsync(p => p.PortfolioId == portfolioId && p.AssetId == assetId);

            if (position == null) return NotFound();
            return position;
        }

        // POST: api/Positions
        // Ajoute un actif dans un portfolio (Position unique)
        [HttpPost]
        public async Task<ActionResult<Position>> PostPosition(Position input)
        {
            // validations de base
            if (input.PortfolioId <= 0) return BadRequest("PortfolioId is required.");
            if (input.AssetId <= 0) return BadRequest("AssetId is required.");
            if (input.Quantity <= 0) return BadRequest("Quantity must be > 0.");
            if (input.AvgBuyPrice <= 0) return BadRequest("AvgBuyPrice must be > 0.");
            if (input.BuyDate == default) return BadRequest("BuyDate is required.");

            // Portfolio existe ?
            bool portfolioExists = await _context.Portfolio.AnyAsync(p => p.Id == input.PortfolioId);
            if (!portfolioExists) return BadRequest("Portfolio not found.");

            // Asset existe ?
            bool assetExists = await _context.Asset.AnyAsync(a => a.Id == input.AssetId);
            if (!assetExists) return BadRequest("Asset not found.");

            // Unicité (PortfolioId, AssetId)
            bool positionExists = await _context.Position.AnyAsync(p =>
                p.PortfolioId == input.PortfolioId && p.AssetId == input.AssetId);

            if (positionExists) return Conflict("This asset already exists in the portfolio.");

            var position = new Position
            {
                PortfolioId = input.PortfolioId,
                AssetId = input.AssetId,
                Quantity = input.Quantity,
                AvgBuyPrice = input.AvgBuyPrice,
                BuyDate = input.BuyDate,
                CreatedAt = DateTime.UtcNow
            };

            _context.Position.Add(position);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPosition),
                new { portfolioId = position.PortfolioId, assetId = position.AssetId },
                position);
        }

        // PUT: api/Positions/{portfolioId}/{assetId}
        // Update contrôlé : ne change pas les IDs
        [HttpPut("{portfolioId:int}/{assetId:int}")]
        public async Task<IActionResult> PutPosition(int portfolioId, int assetId, Position input)
        {
            var position = await _context.Position
                .FirstOrDefaultAsync(p => p.PortfolioId == portfolioId && p.AssetId == assetId);

            if (position == null) return NotFound();

            if (input.Quantity <= 0) return BadRequest("Quantity must be > 0.");
            if (input.AvgBuyPrice <= 0) return BadRequest("AvgBuyPrice must be > 0.");
            if (input.BuyDate == default) return BadRequest("BuyDate is required.");

            // Champs autorisés
            position.Quantity = input.Quantity;
            position.AvgBuyPrice = input.AvgBuyPrice;
            position.BuyDate = input.BuyDate;

            // Champs sensibles ignorés : PortfolioId, AssetId, CreatedAt
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/Positions/{portfolioId}/{assetId}
        [HttpDelete("{portfolioId:int}/{assetId:int}")]
        public async Task<IActionResult> DeletePosition(int portfolioId, int assetId)
        {
            var position = await _context.Position
                .FirstOrDefaultAsync(p => p.PortfolioId == portfolioId && p.AssetId == assetId);

            if (position == null) return NotFound();

            _context.Position.Remove(position);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
