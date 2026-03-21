using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{

    // Gestion des positions — table d'association entre Portfolio et Asset.
    // Une position répond à : "Dans CE portefeuille, je détiens CET actif,
    // en CETTE quantité, acheté à CE prix moyen le CETTE date."
    //
    // Règles importantes :
    //   - La clé est composite (PortfolioId, AssetId) — un actif une fois par portefeuille.
    //   - PortfolioId et AssetId sont immuables après création.
    //   - Seuls Quantity, AvgBuyPrice et BuyDate sont modifiables via PUT.
    //   - Quantity et AvgBuyPrice doivent toujours être strictement positifs.
    [Route("api/[controller]")]
    [ApiController]
    public class PositionsController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PositionsController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // Retourne une position par sa clé composite (PortfolioId, AssetId).
        // L'actif est inclus dans la réponse (Include Asset) pour afficher
        // Ticker, Name et Currency sans requête supplémentaire.
        [HttpGet("{portfolioId:int}/{assetId:int}")]
        public async Task<ActionResult<Position>> GetById(int portfolioId, int assetId)
        {
            var position = await _context.Position
                .Include(p => p.Asset)     // inclut l'actif pour Ticker, Name, Currency
                .FirstOrDefaultAsync(p =>
                    p.PortfolioId == portfolioId &&
                    p.AssetId == assetId);

            if (position is null)
                return NotFound(
                    $"Aucune position trouvée pour le portefeuille {portfolioId} " +
                    $"et l'actif {assetId}.");

            return position;
        }

        // Create suit toujours cet ordre : valider -> vérifier existence -> vérifier unicité -> construire.
        // BuyDate.Date : on enlève l'heure, on garde juste la date -> évite les doublons.
        // CreatedAt imposé par le serveur.
        // Conflict 409 si l'actif est déjà dans ce portefeuille -> utiliser PUT pour mettre à jour.
        [HttpPost]
        public async Task<ActionResult<Position>> Create([FromBody] Position input)
        {
            if (input.PortfolioId <= 0)
                return BadRequest("PortfolioId est requis et doit être positif.");

            if (input.AssetId <= 0)
                return BadRequest("AssetId est requis et doit être positif.");

            if (input.Quantity <= 0)
                return BadRequest("La quantité doit être strictement positive.");

            if (input.AvgBuyPrice <= 0)
                return BadRequest("Le prix moyen d'achat doit être strictement positif.");

            if (input.BuyDate == default)
                return BadRequest("La date d'achat est requise.");

            if (input.BuyDate.Date > DateTime.UtcNow.Date)
                return BadRequest("La date d'achat ne peut pas être dans le futur.");

            bool portfolioExists = await _context.Portfolio
                .AnyAsync(p => p.Id == input.PortfolioId);

            if (!portfolioExists)
                return NotFound($"Portefeuille {input.PortfolioId} introuvable.");

            bool assetExists = await _context.Asset
                .AnyAsync(a => a.Id == input.AssetId);

            if (!assetExists)
                return NotFound($"Actif {input.AssetId} introuvable.");

            // Un actif ne peut apparaître qu'une seule fois dans un portefeuille.
            // Pour augmenter une position existante, utiliser PUT pour mettre à jour
            // Quantity et AvgBuyPrice.
            bool positionExists = await _context.Position
                .AnyAsync(p =>
                    p.PortfolioId == input.PortfolioId &&
                    p.AssetId == input.AssetId);

            if (positionExists)
                return Conflict(
                    $"L'actif {input.AssetId} est déjà présent dans le portefeuille " +
                    $"{input.PortfolioId}. " +
                    "Utilisez PUT /api/Positions/{portfolioId}/{assetId} pour " +
                    "mettre à jour la quantité ou le prix moyen d'achat.");

            // Construction de la position 
            var position = new Position
            {
                PortfolioId = input.PortfolioId,
                AssetId = input.AssetId,
                Quantity = input.Quantity,
                AvgBuyPrice = input.AvgBuyPrice,
                BuyDate = input.BuyDate.Date,   // normalisation : date seule sans heure
                CreatedAt = DateTime.UtcNow
            };

            _context.Position.Add(position);
            await _context.SaveChangesAsync();

            // CreatedAtAction → retourne 201 Created avec l'URL de la nouvelle position.
            // La clé est composite (portfolioId + assetId) donc on passe les deux valeurs
            // pour que ASP.NET puisse construire l'URL complète /api/Positions/{portfolioId}/{assetId}.
            return CreatedAtAction(
                nameof(GetById),
                new { portfolioId = position.PortfolioId, assetId = position.AssetId },
                position);
        }

        
        // Met à jour une position existante.
        // Champs modifiables : Quantity, AvgBuyPrice, BuyDate.
        // Champs immuables   : PortfolioId, AssetId, CreatedAt.
        // Cas d'usage typique : l'utilisateur rachète des actions supplémentaires
        // et veut mettre à jour sa quantité totale et son prix moyen recalculé.
        [HttpPut("{portfolioId:int}/{assetId:int}")]
        public async Task<IActionResult> Update(
            int portfolioId, int assetId,
            [FromBody] Position input)
        {
            var position = await _context.Position
                .FirstOrDefaultAsync(p =>
                    p.PortfolioId == portfolioId &&
                    p.AssetId == assetId);

            if (position is null)
                return NotFound(
                    $"Aucune position trouvée pour le portefeuille {portfolioId} " +
                    $"et l'actif {assetId}.");

            // Validations 
            if (input.Quantity <= 0)
                return BadRequest("La quantité doit être strictement positive.");

            if (input.AvgBuyPrice <= 0)
                return BadRequest("Le prix moyen d'achat doit être strictement positif.");

            if (input.BuyDate == default)
                return BadRequest("La date d'achat est requise.");

            if (input.BuyDate.Date > DateTime.UtcNow.Date)
                return BadRequest("La date d'achat ne peut pas être dans le futur.");

            // Mise à jour des champs autorisés uniquement 
            // PortfolioId, AssetId et CreatedAt ne sont pas touchés
            position.Quantity = input.Quantity;
            position.AvgBuyPrice = input.AvgBuyPrice;
            position.BuyDate = input.BuyDate.Date;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Supprime une position (retire un actif d'un portefeuille).
        // Cette opération est définitive.
        // Le portefeuille et l'actif ne sont pas supprimés — seule la relation
        // (la ligne dans la table Position) est supprimée.
        [HttpDelete("{portfolioId:int}/{assetId:int}")]
        public async Task<IActionResult> Delete(int portfolioId, int assetId)
        {
            var position = await _context.Position
                .FirstOrDefaultAsync(p =>
                    p.PortfolioId == portfolioId &&
                    p.AssetId == assetId);

            if (position is null)
                return NotFound(
                    $"Aucune position trouvée pour le portefeuille {portfolioId} " +
                    $"et l'actif {assetId}.");

            _context.Position.Remove(position);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
