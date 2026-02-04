using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssetPricesController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public AssetPricesController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // GET: api/AssetPrices
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AssetPrice>>> GetAssetPrice()
        {
            return await _context.AssetPrice.ToListAsync();
        }

        // GET: api/AssetPrices/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AssetPrice>> GetAssetPrice(int id)
        {
            var assetPrice = await _context.AssetPrice.FindAsync(id);

            if (assetPrice == null)
            {
                return NotFound();
            }

            return assetPrice;
        }

        // PUT: api/AssetPrices/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAssetPrice(int id, AssetPrice assetPrice)
        {
            if (id != assetPrice.Id)
            {
                return BadRequest();
            }

            _context.Entry(assetPrice).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AssetPriceExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/AssetPrices
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<AssetPrice>> PostAssetPrice(AssetPrice assetPrice)
        {
            _context.AssetPrice.Add(assetPrice);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetAssetPrice", new { id = assetPrice.Id }, assetPrice);
        }

        // DELETE: api/AssetPrices/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAssetPrice(int id)
        {
            var assetPrice = await _context.AssetPrice.FindAsync(id);
            if (assetPrice == null)
            {
                return NotFound();
            }

            _context.AssetPrice.Remove(assetPrice);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool AssetPriceExists(int id)
        {
            return _context.AssetPrice.Any(e => e.Id == id);
        }
    }
}
