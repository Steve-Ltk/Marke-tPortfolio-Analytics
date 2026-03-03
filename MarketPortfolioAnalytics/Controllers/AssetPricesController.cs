using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Services;

namespace MarketPortfolioAnalytics.Controllers
{
    /// <summary>
    /// Gestion des prix historiques de marché (table AssetPrice).
    ///
    /// Règles importantes :
    ///   - Un seul prix par (AssetId, Date) — unicité enforced en base ET ici.
    ///   - AssetId et Date sont immuables après insertion.
    ///   - Seuls Open, High, Low, Close, Volume sont modifiables via PUT.
    ///   - Close est toujours obligatoire et strictement positif.
    ///   - La synchronisation FMP insère les nouveaux prix et ignore les doublons.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AssetPricesController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly FmpService _fmp;

        public AssetPricesController(
            MarketPortfolioAnalyticsContext context,
            FmpService fmp)
        {
            _context = context;
            _fmp = fmp;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne l'historique de prix d'un actif, avec filtres optionnels.
        ///
        /// Exemples :
        ///   GET /api/AssetPrices/by-asset/3
        ///   GET /api/AssetPrices/by-asset/3?from=2023-01-01
        ///   GET /api/AssetPrices/by-asset/3?from=2023-01-01&amp;to=2024-01-01
        ///
        /// Les résultats sont triés du plus ancien au plus récent.
        /// C'est l'endpoint principal — les calculs financiers consomment cette série.
        /// </summary>
        // GET api/AssetPrices/by-asset/3?from=2023-01-01&to=2024-01-01
        [HttpGet("by-asset/{assetId}")]
        public async Task<ActionResult<IEnumerable<AssetPrice>>> GetByAsset(
            int assetId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            // Vérifie que l'actif existe
            bool assetExists = await _context.Asset
                .AnyAsync(a => a.Id == assetId);

            if (!assetExists)
                return NotFound($"Actif {assetId} introuvable.");

            // Construction de la requête avec filtres optionnels
            var query = _context.AssetPrice
                .Where(ap => ap.AssetId == assetId);

            if (from.HasValue)
                query = query.Where(ap => ap.Date >= from.Value.Date);

            if (to.HasValue)
                query = query.Where(ap => ap.Date <= to.Value.Date);

            // Tri du plus ancien au plus récent (ordre naturel pour les calculs)
            var prices = await query
                .OrderBy(ap => ap.Date)
                .ToListAsync();

            return prices;
        }

        /// <summary>
        /// Retourne un prix par son Id.
        /// </summary>
        // GET api/AssetPrices/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AssetPrice>> GetById(int id)
        {
            var price = await _context.AssetPrice.FindAsync(id);

            if (price is null)
                return NotFound($"Prix {id} introuvable.");

            return price;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CRÉATION MANUELLE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ajoute un prix manuellement pour un actif à une date donnée.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "assetId": 3,
        ///   "date":    "2024-01-15",
        ///   "open":    183.63,        ← optionnel
        ///   "high":    184.26,        ← optionnel
        ///   "low":     182.42,        ← optionnel
        ///   "close":   183.31,        ← obligatoire, > 0
        ///   "volume":  49765800       ← optionnel
        /// }
        ///
        /// Retourne 409 Conflict si un prix existe déjà pour cet actif à cette date.
        /// </summary>
        // POST api/AssetPrices
        [HttpPost]
        public async Task<ActionResult<AssetPrice>> Create([FromBody] AssetPrice input)
        {
            // ── Validations ───────────────────────────────────────────────────
            if (input.AssetId <= 0)
                return BadRequest("AssetId est requis et doit être positif.");

            if (input.Date == default)
                return BadRequest("La date est requise.");

            if (input.Close <= 0)
                return BadRequest("Le prix de clôture (Close) doit être strictement positif.");

            if (input.Open.HasValue && input.Open <= 0)
                return BadRequest("Le prix d'ouverture (Open) doit être positif.");

            if (input.High.HasValue && input.Low.HasValue && input.High < input.Low)
                return BadRequest("Le prix High ne peut pas être inférieur au prix Low.");

            // ── Existence de l'actif ──────────────────────────────────────────
            bool assetExists = await _context.Asset
                .AnyAsync(a => a.Id == input.AssetId);

            if (!assetExists)
                return NotFound($"Actif {input.AssetId} introuvable.");

            // ── Unicité (AssetId, Date) ───────────────────────────────────────
            // Vérification applicative en complément de l'index unique en base
            bool alreadyExists = await _context.AssetPrice
                .AnyAsync(ap => ap.AssetId == input.AssetId
                             && ap.Date.Date == input.Date.Date);

            if (alreadyExists)
                return Conflict(
                    $"Un prix existe déjà pour l'actif {input.AssetId} " +
                    $"à la date {input.Date:yyyy-MM-dd}.");

            // ── Construction de l'entité ──────────────────────────────────────
            // On normalise la date à minuit pour éviter les doublons
            // dus aux heures différentes (ex: 2024-01-15 09:30 vs 2024-01-15 16:00)
            var price = new AssetPrice
            {
                AssetId = input.AssetId,
                Date = input.Date.Date,   // normalisation : on garde uniquement la date
                Open = input.Open,
                High = input.High,
                Low = input.Low,
                Close = input.Close,
                Volume = input.Volume
            };

            _context.AssetPrice.Add(price);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = price.Id }, price);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SYNCHRONISATION DEPUIS FMP
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Synchronise les prix historiques d'un actif depuis l'API FMP.
        ///
        /// Cette route appelle GET /stable/historical-prices sur FMP,
        /// puis insère en base uniquement les prix qui n'existent pas encore.
        /// Les doublons sont silencieusement ignorés (pas d'erreur).
        ///
        /// Corps JSON attendu :
        /// {
        ///   "from": "2023-01-01",   ← requis
        ///   "to":   "2024-01-01"    ← requis
        /// }
        ///
        /// Retourne un résumé : combien de prix ont été insérés / ignorés.
        ///
        /// Exemple d'usage : après avoir ajouté un actif via stocks/from-fmp,
        /// appeler cette route pour charger 2 ans d'historique.
        /// </summary>
        // POST api/AssetPrices/sync/3
        [HttpPost("sync/{assetId}")]
        public async Task<ActionResult<SyncResult>> SyncFromFmp(
            int assetId,
            [FromBody] SyncRequest request)
        {
            // ── Validation des dates ──────────────────────────────────────────
            if (request.From == default || request.To == default)
                return BadRequest("Les dates 'from' et 'to' sont requises.");

            if (request.From > request.To)
                return BadRequest("La date 'from' doit être antérieure ou égale à 'to'.");

            if (request.To > DateTime.UtcNow.Date)
                return BadRequest("La date 'to' ne peut pas être dans le futur.");

            // ── Existence de l'actif ──────────────────────────────────────────
            var asset = await _context.Asset.FindAsync(assetId);

            if (asset is null)
                return NotFound($"Actif {assetId} introuvable.");

            // ── Appel FMP — /stable/historical-prices ─────────────────────────
            // FmpService retourne une List<FmpHistoricalPrice>
            // Vide si FMP ne retourne rien ou en cas d'erreur réseau

            List<FmpHistoricalPrice> fmpPrices = await _fmp.GetHistoricalPricesAsync(
                asset.Ticker, request.From, request.To);

            if (fmpPrices.Count == 0)
                return Ok(new SyncResult(
                    Ticker: asset.Ticker,
                    From: request.From,
                    To: request.To,
                    Fetched: 0,
                    Inserted: 0,
                    Skipped: 0,
                    Message: "FMP n'a retourné aucun prix pour cette période. " +
                              "Vérifiez que l'actif est coté sur FMP et que la période est valide."
                ));

            // ── Récupération des dates déjà présentes en base ─────────────────
            // On charge uniquement les dates (pas tout l'objet) pour optimiser la mémoire
            // On restreint la requête à la période concernée pour ne pas charger tout l'historique

            // ToHashSetAsync n'existe pas dans EF Core — on passe par ToListAsync
            // puis on convertit en HashSet pour conserver la vérification en O(1)
            var existingDates = (await _context.AssetPrice
                .Where(ap => ap.AssetId == assetId
                          && ap.Date >= request.From.Date
                          && ap.Date <= request.To.Date)
                .Select(ap => ap.Date.Date)
                .ToListAsync())
                .ToHashSet();   // HashSet pour O(1) sur la vérification

            // ── Filtrage et construction des nouveaux prix ────────────────────
            // On n'insère que les prix dont la date n'est pas encore en base

            var toInsert = fmpPrices
                .Where(p => !existingDates.Contains(p.Date.Date))
                .Select(p => new AssetPrice
                {
                    AssetId = assetId,
                    Date = p.Date.Date,   // normalisation de la date
                    Open = p.Open,
                    High = p.High,
                    Low = p.Low,
                    Close = p.Close,
                    Volume = p.Volume
                })
                .ToList();

            int skipped = fmpPrices.Count - toInsert.Count;

            // ── Insertion en masse ────────────────────────────────────────────
            // AddRange est plus efficace qu'une boucle d'Add individuels
            // EF génère un seul INSERT groupé
            if (toInsert.Count > 0)
            {
                _context.AssetPrice.AddRange(toInsert);
                await _context.SaveChangesAsync();
            }

            return Ok(new SyncResult(
                Ticker: asset.Ticker,
                From: request.From,
                To: request.To,
                Fetched: fmpPrices.Count,
                Inserted: toInsert.Count,
                Skipped: skipped,
                Message: toInsert.Count > 0
                    ? $"{toInsert.Count} prix insérés avec succès."
                    : "Tous les prix de cette période sont déjà en base."
            ));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Corrige les valeurs OHLCV d'un prix existant.
        ///
        /// Champs modifiables : Open, High, Low, Close, Volume.
        /// Champs immuables   : AssetId, Date, Id.
        ///
        /// AssetId et Date sont immuables car ils constituent la clé fonctionnelle
        /// de l'enregistrement. Les modifier reviendrait à créer un doublon
        /// ou à casser la cohérence de l'historique.
        /// </summary>
        // PUT api/AssetPrices/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] AssetPrice input)
        {
            var price = await _context.AssetPrice.FindAsync(id);

            if (price is null)
                return NotFound($"Prix {id} introuvable.");

            // ── Validations ───────────────────────────────────────────────────
            if (input.Close <= 0)
                return BadRequest("Le prix de clôture (Close) doit être strictement positif.");

            if (input.Open.HasValue && input.Open <= 0)
                return BadRequest("Le prix d'ouverture (Open) doit être positif.");

            if (input.High.HasValue && input.Low.HasValue && input.High < input.Low)
                return BadRequest("Le prix High ne peut pas être inférieur au prix Low.");

            // ── Mise à jour des champs OHLCV uniquement ───────────────────────
            // AssetId et Date ne sont pas touchés
            price.Open = input.Open;
            price.High = input.High;
            price.Low = input.Low;
            price.Close = input.Close;
            price.Volume = input.Volume;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Supprime un prix par son Id.
        /// Utile pour corriger une erreur d'importation.
        /// </summary>
        // DELETE api/AssetPrices/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var price = await _context.AssetPrice.FindAsync(id);

            if (price is null)
                return NotFound($"Prix {id} introuvable.");

            _context.AssetPrice.Remove(price);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RECORDS — objets de requête et de réponse spécifiques à ce contrôleur
    // Définis ici car ils ne sont utilisés que par AssetPricesController
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Corps de la requête de synchronisation FMP.
    /// </summary>
    public record SyncRequest(DateTime From, DateTime To);

    /// <summary>
    /// Résumé retourné après une synchronisation FMP.
    /// Indique combien de prix ont été récupérés, insérés et ignorés.
    /// </summary>
    public record SyncResult(
        string Ticker,
        DateTime From,
        DateTime To,
        int Fetched,    // nombre de prix reçus de FMP
        int Inserted,   // nombre de prix insérés en base
        int Skipped,    // nombre de doublons ignorés
        string Message
    );
}
