using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{

    // Gestion des portefeuilles financiers.
    // Règles importantes :
    //   - GET /api/Portfolios exige un userId — on n'expose jamais tous les portefeuilles.
    //   - Seuls Name et Currency sont modifiables via PUT.
    //   - UserId et CreatedAt sont immuables après création.
    //   - Un portefeuille contenant des positions ne peut pas être supprimé directement.
    [Route("api/[controller]")]
    [ApiController]
    public class PortfoliosController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PortfoliosController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // [FromQuery] : userId vient de l'URL → /api/Portfolios?userId=3
        // userId obligatoire : sans filtre on exposerait les données de tous les users.
        // On vérifie aussi que l'utilisateur existe et est actif avant de chercher.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Portfolio>>> GetByUser(
            [FromQuery] int? userId)
        {
            // userId obligatoire — on refuse la requête sans filtre
            if (userId is null)
                return BadRequest(
                    "Le paramètre 'userId' est requis. " +
                    "Exemple : GET /api/Portfolios?userId=1");

            // Vérification que l'utilisateur existe et est actif
            bool userExists = await _context.AppUser
                .AnyAsync(u => u.Id == userId && u.IsActive);

            if (!userExists)
                return NotFound($"Utilisateur {userId} introuvable ou inactif.");

            var portfolios = await _context.Portfolio
                .Where(p => p.UserId == userId)
                .ToListAsync();

            return portfolios;
        }

        // Retourne un portefeuille par son Id, sans ses positions.
        // Pour avoir les positions, utiliser GET /api/Portfolios/{id}/details.
        [HttpGet("{id}")]
        public async Task<ActionResult<Portfolio>> GetById(int id)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            return portfolio;
        }

        // Include + ThenInclude : charge les positions ET leurs actifs en une seule requête SQL.
        // Sans Include -> ListePositions serait null (EF Core ne charge pas automatiquement).
        // ThenInclude -> pour chaque position, charge aussi l'actif associé.
        [HttpGet("{id}/details")]
        public async Task<ActionResult<Portfolio>> GetDetails(int id)
        {
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)      // charge les positions
                    .ThenInclude(pos => pos.Asset)    // pour chaque position, charge l'actif
                .FirstOrDefaultAsync(p => p.Id == id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            return portfolio;
        }

        // Retourne uniquement les positions d'un portefeuille, avec les actifs associés.
        // Différence avec /details : retourne une liste de positions
        // et non le portefeuille complet. Plus adapté si on veut juste la liste.
        [HttpGet("{id}/positions")]
        public async Task<ActionResult<IEnumerable<Position>>> GetPositions(int id)
        {
            bool portfolioExists = await _context.Portfolio
                .AnyAsync(p => p.Id == id);

            if (!portfolioExists)
                return NotFound($"Portefeuille {id} introuvable.");

            var positions = await _context.Position
                .Include(p => p.Asset)       // inclut l'actif pour afficher Ticker, Name...
                .Where(p => p.PortfolioId == id)
                .ToListAsync();

            return positions;
        }

        // CreatedAt imposé par le serveur → le client ne peut pas choisir la date de création.
        // Currency normalisée en majuscules et validée → "eur" devient "EUR".
        // Regex [A-Z]{3} → exactement 3 lettres majuscules, norme ISO 4217.
        // POST api/Portfolios
        [HttpPost]
        public async Task<ActionResult<Portfolio>> Create([FromBody] Portfolio input)
        {
            // Validation du nom 
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Le nom du portefeuille est requis.");

            // Validation de l'utilisateur 
            if (input.UserId <= 0)
                return BadRequest("UserId est requis et doit être positif.");

            bool userExists = await _context.AppUser
                .AnyAsync(u => u.Id == input.UserId && u.IsActive);

            if (!userExists)
                return BadRequest(
                    $"Utilisateur {input.UserId} introuvable ou inactif. " +
                    "Impossible de créer un portefeuille pour cet utilisateur.");

            // Validation de la devise
            // Si non fournie, on applique EUR par défaut
            string currency = string.IsNullOrWhiteSpace(input.Currency)
                ? "EUR"
                : input.Currency.Trim().ToUpper();

            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return BadRequest(
                    "La devise doit être un code ISO 4217 à 3 lettres (ex: EUR, USD, GBP).");

            // Construction de l'entité 
            var portfolio = new Portfolio
            {
                Name = input.Name.Trim(),
                Currency = currency,
                UserId = input.UserId,
                CreatedAt = DateTime.UtcNow   // imposé par le serveur
            };

            _context.Portfolio.Add(portfolio);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = portfolio.Id }, portfolio);
        }

        // Met à jour le nom et/ou la devise d'un portefeuille.
        // Champs modifiables : Name, Currency.
        // Champs immuables   : UserId, CreatedAt.
        // UserId est immuable : un portefeuille ne change pas de propriétaire.
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Portfolio input)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            // Name 
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Le nom du portefeuille est requis.");

            portfolio.Name = input.Name.Trim();

            // Currency 
            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                string currency = input.Currency.Trim().ToUpper();

                if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                    return BadRequest(
                        "La devise doit être un code ISO 4217 à 3 lettres (ex: EUR, USD, GBP).");

                portfolio.Currency = currency;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // On bloque la suppression si le portefeuille contient des positions.
        // Cascade en base supprimerait tout automatiquement, mais on préfère bloquer
        // pour éviter une suppression accidentelle → l'user supprime d'abord ses positions.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            // Vérifie si le portefeuille contient des positions
            bool hasPositions = await _context.Position
                .AnyAsync(p => p.PortfolioId == id);

            if (hasPositions)
                return BadRequest(
                    $"Impossible de supprimer le portefeuille '{portfolio.Name}' : " +
                    "il contient des positions. " +
                    "Supprimez d'abord toutes les positions via " +
                    "DELETE /api/Positions/{portfolioId}/{assetId}.");

            _context.Portfolio.Remove(portfolio);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
