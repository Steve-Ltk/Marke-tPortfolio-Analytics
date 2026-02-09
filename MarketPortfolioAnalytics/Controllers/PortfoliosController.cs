using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PortfoliosController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PortfoliosController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // GET: api/Portfolios
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Portfolio>>> GetPortfolio()
        {
            return await _context.Portfolio.ToListAsync();
        }

        // GET: api/Portfolios/5
        // Retourne seulement le portfolio (sans positions)
        [HttpGet("{id}")]
        public async Task<ActionResult<Portfolio>> GetPortfolio(int id)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);
            if (portfolio == null) return NotFound();
            return portfolio;
        }

        // GET: api/Portfolios/5/details
        // Retourne le portfolio + positions + assets
        [HttpGet("{id}/details")]
        public async Task<ActionResult<Portfolio>> GetPortfolioDetails(int id)
        {
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (portfolio == null)
                return NotFound();

            return portfolio;
        }

        // POST: api/Portfolios
        [HttpPost]
        public async Task<ActionResult<Portfolio>> PostPortfolio(Portfolio input)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Name is required.");

            // Currency : par défaut EUR si non fourni
            var currency = string.IsNullOrWhiteSpace(input.Currency)
                ? "EUR"
                : input.Currency.Trim().ToUpper();

            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return BadRequest("Currency must be a 3-letter code (e.g., EUR, USD).");

            if (input.UserId <= 0)
                return BadRequest("UserId is required.");

            bool userOk = await _context.AppUser.AnyAsync(u => u.Id == input.UserId && u.IsActive);
            if (!userOk)
                return BadRequest("UserId is invalid or user is inactive.");

            var portfolio = new Portfolio
            {
                Name = input.Name.Trim(),
                Currency = currency,
                CreatedAt = DateTime.UtcNow,
                UserId = input.UserId
            };

            _context.Portfolio.Add(portfolio);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPortfolio), new { id = portfolio.Id }, portfolio);
        }


        // PUT: api/Portfolios/5
        // Update contrôlé: on autorise uniquement Name et Currency (pas UserId, pas CreatedAt)
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPortfolio(int id, Portfolio input)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);
            if (portfolio == null) return NotFound();

            // Name obligatoire
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Name is required.");

            // Currency obligatoire
            var currency = string.IsNullOrWhiteSpace(input.Currency)
            ? "EUR"
            : input.Currency.Trim().ToUpper();

            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return BadRequest("Currency must be a 3-letter code (e.g., EUR, USD).");


            // Champs autorisés
            portfolio.Name = input.Name.Trim();
            portfolio.Currency = currency;

            // Champs sensibles ignorés:
            // portfolio.UserId = portfolio.UserId;
            // portfolio.CreatedAt = portfolio.CreatedAt;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // GET: api/Portfolios/5/positions
        [HttpGet("{id}/positions")]
        public async Task<ActionResult<IEnumerable<Position>>> GetPortfolioPositions(int id)
        {
            var portfolioExists = await _context.Portfolio.AnyAsync(p => p.Id == id);
            if (!portfolioExists) return NotFound("Portfolio not found.");

            var positions = await _context.Position
                .Where(p => p.PortfolioId == id)
                .ToListAsync();

            return positions;
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePortfolio(int id)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);
            if (portfolio == null) return NotFound();

            bool hasPositions = await _context.Position.AnyAsync(p => p.PortfolioId == id);
            if (hasPositions)
                return BadRequest("Cannot delete a portfolio that has positions. Delete positions first.");

            _context.Portfolio.Remove(portfolio);
            await _context.SaveChangesAsync();

            return NoContent();
        }

    }
}
