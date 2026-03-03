using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{
    /// <summary>
    /// Gestion des portefeuilles financiers.
    ///
    /// Règles importantes :
    ///   - GET /api/Portfolios exige un userId — on n'expose jamais tous les portefeuilles.
    ///   - Seuls Name et Currency sont modifiables via PUT.
    ///   - UserId et CreatedAt sont immuables après création.
    ///   - Un portefeuille contenant des positions ne peut pas être supprimé directement.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PortfoliosController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PortfoliosController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne les portefeuilles d'un utilisateur.
        ///
        /// Le paramètre userId est obligatoire.
        /// On refuse de retourner tous les portefeuilles sans filtre utilisateur
        /// pour éviter d'exposer les données de tous les utilisateurs.
        ///
        /// Exemple : GET /api/Portfolios?userId=1
        /// </summary>
        // GET api/Portfolios?userId=1
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

        /// <summary>
        /// Retourne un portefeuille par son Id, sans ses positions.
        /// Pour avoir les positions, utiliser GET /api/Portfolios/{id}/details.
        /// </summary>
        // GET api/Portfolios/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Portfolio>> GetById(int id)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            return portfolio;
        }

        /// <summary>
        /// Retourne un portefeuille avec ses positions et les détails de chaque actif.
        ///
        /// Utilise Include + ThenInclude pour charger en une seule requête SQL :
        ///   Portfolio → ListePositions → Asset (avec son type réel Stock ou Bond)
        ///
        /// C'est l'endpoint utilisé pour afficher le détail complet d'un portefeuille.
        /// </summary>
        // GET api/Portfolios/5/details
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

        /// <summary>
        /// Retourne uniquement les positions d'un portefeuille, avec les actifs associés.
        ///
        /// Différence avec /details : retourne une liste de positions
        /// et non le portefeuille complet. Plus adapté si on veut juste la liste.
        /// </summary>
        // GET api/Portfolios/5/positions
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

        // ═══════════════════════════════════════════════════════════════════════
        // CRÉATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Crée un nouveau portefeuille pour un utilisateur actif.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "name":     "Mon portefeuille tech",  ← requis
        ///   "currency": "EUR",                    ← optionnel, défaut EUR
        ///   "userId":   1                         ← requis
        /// }
        ///
        /// Champs imposés par le serveur (ignorés si fournis) :
        ///   - CreatedAt → DateTime.UtcNow
        /// </summary>
        // POST api/Portfolios
        [HttpPost]
        public async Task<ActionResult<Portfolio>> Create([FromBody] Portfolio input)
        {
            // ── Validation du nom ─────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Le nom du portefeuille est requis.");

            // ── Validation de l'utilisateur ───────────────────────────────────
            if (input.UserId <= 0)
                return BadRequest("UserId est requis et doit être positif.");

            bool userExists = await _context.AppUser
                .AnyAsync(u => u.Id == input.UserId && u.IsActive);

            if (!userExists)
                return BadRequest(
                    $"Utilisateur {input.UserId} introuvable ou inactif. " +
                    "Impossible de créer un portefeuille pour cet utilisateur.");

            // ── Validation de la devise ───────────────────────────────────────
            // Si non fournie, on applique EUR par défaut
            string currency = string.IsNullOrWhiteSpace(input.Currency)
                ? "EUR"
                : input.Currency.Trim().ToUpper();

            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return BadRequest(
                    "La devise doit être un code ISO 4217 à 3 lettres (ex: EUR, USD, GBP).");

            // ── Construction de l'entité ──────────────────────────────────────
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

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met à jour le nom et/ou la devise d'un portefeuille.
        ///
        /// Champs modifiables : Name, Currency.
        /// Champs immuables   : UserId, CreatedAt.
        ///
        /// UserId est immuable : un portefeuille ne change pas de propriétaire.
        /// </summary>
        // PUT api/Portfolios/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Portfolio input)
        {
            var portfolio = await _context.Portfolio.FindAsync(id);

            if (portfolio is null)
                return NotFound($"Portefeuille {id} introuvable.");

            // ── Name ──────────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(input.Name))
                return BadRequest("Le nom du portefeuille est requis.");

            portfolio.Name = input.Name.Trim();

            // ── Currency ──────────────────────────────────────────────────────
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

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Supprime un portefeuille vide (sans positions).
        ///
        /// Bloqué si le portefeuille contient des positions.
        /// L'utilisateur doit d'abord supprimer toutes ses positions
        /// via DELETE /api/Positions/{portfolioId}/{assetId}.
        ///
        /// Note : la contrainte Cascade dans le DbContext supprimerait les positions
        /// automatiquement si on supprimait directement en base, mais on préfère
        /// bloquer ici pour éviter une suppression accidentelle de données.
        /// </summary>
        // DELETE api/Portfolios/5
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
