using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Services;

namespace MarketPortfolioAnalytics.Controllers
{
    /// <summary>
    /// Gestion des actifs financiers (actions et obligations).
    ///
    /// Règles importantes :
    ///   - On ne crée jamais un Asset de base — uniquement des Stock ou Bond.
    ///   - La création passe toujours par FMP pour valider le ticker
    ///     et récupérer les métadonnées (Name, Currency, Exchange, Sector, ISIN).
    ///   - Le ticker est normalisé en majuscules et doit être unique en base.
    ///   - Un actif utilisé dans une ou plusieurs positions ne peut pas être supprimé.
    /// </summary>
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

        // ═══════════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne tous les actifs enregistrés dans la plateforme.
        /// </summary>
        // GET api/Assets
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Asset>>> GetAll()
        {
            return await _context.Asset.ToListAsync();
        }

        /// <summary>
        /// Retourne un actif par son Id avec toutes ses propriétés spécifiques.
        /// </summary>
        // GET api/Assets/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Asset>> GetById(int id)
        {
            var stock = await _context.Set<Stock>()
                .FirstOrDefaultAsync(s => s.Id == id);
            if (stock is not null) return stock;

            var bond = await _context.Set<Bond>()
                .FirstOrDefaultAsync(b => b.Id == id);
            if (bond is not null) return bond;

            var asset = await _context.Asset.FindAsync(id);
            if (asset is null)
                return NotFound($"Actif {id} introuvable.");

            return asset;
        }

        /// <summary>
        /// Retourne un actif par son ticker (ex : "AAPL", "MC.PA").
        /// </summary>
        // GET api/Assets/by-ticker/AAPL
        [HttpGet("by-ticker/{ticker}")]
        public async Task<ActionResult<Asset>> GetByTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return BadRequest("Le ticker est requis.");

            string normalized = ticker.Trim().ToUpper();

            var asset = await _context.Asset
                .FirstOrDefaultAsync(a => a.Ticker == normalized);

            if (asset is null)
                return NotFound($"Aucun actif trouvé pour le ticker '{normalized}'.");

            return asset;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CRÉATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Crée une action (Stock) en récupérant ses métadonnées depuis FMP.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "ticker": "AAPL",          ← requis
        ///   "sector": "Technology",    ← optionnel
        ///   "isin":   "US0378331005"   ← optionnel
        /// }
        ///
        /// IMPORTANT : on accepte un DTO léger (CreateStockRequest) et non le modèle
        /// Stock complet, car Stock hérite de Asset qui a [Required] sur Name,
        /// Currency et CreatedAt — ces champs sont fournis par FMP, pas par le client.
        /// Utiliser Stock directement déclencherait une erreur de validation 400
        /// avant même d'atteindre la méthode.
        /// </summary>
        // POST api/Assets/stocks/from-fmp
        [HttpPost("stocks/from-fmp")]
        public async Task<ActionResult<Asset>> CreateStock([FromBody] CreateStockRequest input)
        {
            if (string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Le ticker est requis.");

            string ticker = input.Ticker.Trim().ToUpper();

            // Unicité du ticker en base avant d'appeler FMP
            if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
                return Conflict($"Un actif avec le ticker '{ticker}' existe déjà.");

            // ── Appel FMP — /stable/profile ───────────────────────────────────
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

                Sector = !string.IsNullOrWhiteSpace(profile.Sector)
                    ? profile.Sector.Trim()
                    : input.Sector?.Trim(),

                ISIN = !string.IsNullOrWhiteSpace(profile.Isin)
                    ? profile.Isin.Trim()
                    : input.ISIN?.Trim()
            };

            _context.Asset.Add(stock);
            await _context.SaveChangesAsync();

            // ── DeclaredType = typeof(Asset) obligatoire ───────────────────────────
            // CreatedAtAction ne propage pas automatiquement le type générique de
            // ActionResult<Asset>. Sans DeclaredType, System.Text.Json sérialise
            // en Stock (type runtime) sans passer par la configuration polymorphique
            // d'Asset → le discriminateur "assetType" n'est jamais émis.
            // Avec DeclaredType = typeof(Asset), STJ détecte que runtimeType != declaredType
            // et ajoute automatiquement "assetType": "Stock".
            var stockResult = CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
            stockResult.DeclaredType = typeof(Asset);
            return stockResult;
        }

        /// <summary>
        /// Crée une obligation (Bond) en récupérant ses métadonnées depuis FMP.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "ticker":       "AAPL3",
        ///   "couponRate":   4.35,
        ///   "maturityDate": "2028-09-15"
        /// }
        /// </summary>
        // POST api/Assets/bonds/from-fmp
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
                return BadRequest(
                    $"Ticker '{ticker}' introuvable sur FMP ou erreur de l'API externe. " +
                    "Vérifiez que le ticker est correct.");

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

            // Même correction que pour CreateStock — DeclaredType force la sérialisation
            // polymorphique et l'émission du discriminateur "assetType": "Bond".
            var bondResult = CreatedAtAction(nameof(GetById), new { id = bond.Id }, bond);
            bondResult.DeclaredType = typeof(Asset);
            return bondResult;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met à jour les métadonnées modifiables d'un actif.
        /// Champs modifiables : Name, Exchange, Currency.
        /// Champs immuables   : Ticker, CreatedAt.
        ///
        /// IMPORTANT : on accepte un DTO léger (UpdateAssetRequest) et non le modèle
        /// Asset complet, car Asset a [Required] sur Name, Ticker, Currency, CreatedAt.
        /// Un PUT avec seulement {"name": "Apple"} déclencherait sinon une erreur 400.
        /// </summary>
        // PUT api/Assets/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateAssetRequest input)
        {
            var asset = await _context.Asset.FindAsync(id);

            if (asset is null)
                return NotFound($"Actif {id} introuvable.");

            if (!string.IsNullOrWhiteSpace(input.Name))
                asset.Name = input.Name.Trim();

            if (input.Exchange is not null)
                asset.Exchange = string.IsNullOrWhiteSpace(input.Exchange)
                    ? null
                    : input.Exchange.Trim();

            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                string currency = input.Currency.Trim().ToUpper();

                if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
                    return BadRequest(
                        "La devise doit être un code ISO 4217 à 3 lettres (ex: USD, EUR, GBP).");

                asset.Currency = currency;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Supprime définitivement un actif et tout son historique de prix.
        /// Bloqué si l'actif est utilisé dans au moins une position.
        /// </summary>
        // DELETE api/Assets/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var asset = await _context.Asset.FindAsync(id);

            if (asset is null)
                return NotFound($"Actif {id} introuvable.");

            bool usedInPosition = await _context.Position
                .AnyAsync(p => p.AssetId == id);

            if (usedInPosition)
                return BadRequest(
                    $"Impossible de supprimer l'actif '{asset.Ticker}' : " +
                    "il est utilisé dans un ou plusieurs portefeuilles. " +
                    "Supprimez d'abord les positions concernées.");

            _context.Asset.Remove(asset);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DTOs — évitent la validation prématurée du modèle Asset/Stock complet
    //
    // Pourquoi des DTOs ici ?
    //   Asset a [Required] sur Name, Currency et CreatedAt.
    //   Lors de POST stocks/from-fmp, le client n'envoie que {"ticker": "AAPL"}.
    //   Sans DTO, ASP.NET rejette la requête en 400 avant d'appeler la méthode.
    //   Avec un DTO léger, la validation du modèle passe et le contrôleur gère
    //   lui-même la validation métier avant d'appeler FMP.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de création d'une action depuis FMP.</summary>
    public record CreateStockRequest(string? Ticker, string? Sector, string? ISIN);

    /// <summary>Corps de la requête de mise à jour d'un actif.</summary>
    public record UpdateAssetRequest(string? Name, string? Exchange, string? Currency);
}