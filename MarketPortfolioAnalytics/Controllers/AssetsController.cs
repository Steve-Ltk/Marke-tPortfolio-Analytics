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
        ///
        /// Le champ "assetType" dans la réponse JSON indique le type réel
        /// de chaque actif ("Stock" ou "Bond") grâce à JsonPolymorphic.
        /// </summary>
        // GET api/Assets
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Asset>>> GetAll()
        {
            return await _context.Asset.ToListAsync();
        }

        /// <summary>
        /// Retourne un actif par son Id avec toutes ses propriétés spécifiques.
        ///
        /// On interroge d'abord Set&lt;Stock&gt;, puis Set&lt;Bond&gt;, puis Asset de base.
        /// Cela garantit qu'on retourne le type le plus précis :
        ///   - un Stock inclura Sector et ISIN
        ///   - un Bond inclura MaturityDate et CouponRate
        /// Si on cherchait directement dans DbSet&lt;Asset&gt;, EF retournerait
        /// un objet Asset de base sans les propriétés spécifiques au sous-type.
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
        /// Le ticker est normalisé en majuscules avant la recherche.
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
        ///   "sector": "Technology",    ← optionnel (ignoré si FMP le fournit)
        ///   "isin":   "US0378331005"   ← optionnel (ignoré si FMP le fournit)
        /// }
        ///
        /// FMP fournit via /stable/profile :
        ///   Symbol, Name, Currency, Exchange, Sector, ISIN.
        ///
        /// Priorité : valeur FMP → valeur fournie par le client → null.
        /// </summary>
        // POST api/Assets/stocks/from-fmp
        [HttpPost("stocks/from-fmp")]
        public async Task<ActionResult<Asset>> CreateStock([FromBody] Stock input)
        {
            if (string.IsNullOrWhiteSpace(input.Ticker))
                return BadRequest("Le ticker est requis.");

            string ticker = input.Ticker.Trim().ToUpper();

            // Unicité du ticker en base avant d'appeler FMP (évite un appel inutile)
            if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
                return Conflict($"Un actif avec le ticker '{ticker}' existe déjà.");

            // ── Appel FMP — /stable/profile ───────────────────────────────────
            // GetProfileAsync retourne un FmpProfile? (record)
            // null = ticker inconnu de FMP ou erreur réseau

            FmpProfile? profile = await _fmp.GetProfileAsync(ticker);

            if (profile is null)
                return BadRequest(
                    $"Ticker '{ticker}' introuvable sur FMP ou erreur de l'API externe. " +
                    "Vérifiez que le ticker est correct (ex: 'AAPL', 'MC.PA').");

            // ── Construction du Stock ─────────────────────────────────────────
            // On accède aux propriétés du record directement (plus de .Value)
            // Profile est un record FmpProfile avec : Symbol, Name, Currency,
            //   Exchange (nullable), Sector (nullable), Isin (nullable)

            var stock = new Stock
            {
                Ticker = profile.Symbol,    // toujours normalisé en majuscules par FmpService
                Name = profile.Name,
                Currency = profile.Currency,
                Exchange = profile.Exchange,  // null si non fourni par FMP
                CreatedAt = DateTime.UtcNow,

                // Sector : FMP en priorité, sinon valeur saisie par le client, sinon null
                Sector = !string.IsNullOrWhiteSpace(profile.Sector)
                    ? profile.Sector.Trim()
                    : input.Sector?.Trim(),

                // ISIN : FMP en priorité, sinon valeur saisie par le client, sinon null
                ISIN = !string.IsNullOrWhiteSpace(profile.Isin)
                    ? profile.Isin.Trim()
                    : input.ISIN?.Trim()
            };

            // Add sur DbSet<Asset> : EF détecte que c'est un Stock
            // et insère dans les tables "Asset" ET "Stock" automatiquement (TPT)
            _context.Asset.Add(stock);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
        }

        /// <summary>
        /// Crée une obligation (Bond) en récupérant ses métadonnées depuis FMP.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "ticker":       "AAPL3",       ← requis (ticker de l'obligation sur FMP)
        ///   "couponRate":   4.35,           ← optionnel (ignoré si FMP le fournit)
        ///   "maturityDate": "2028-09-15"   ← optionnel (ignoré si FMP le fournit)
        /// }
        ///
        /// FMP fournit via /stable/profile : Symbol, Name, Currency, Exchange.
        /// FMP fournit via /stable/company-notes : CouponRate, MaturityDate (si disponible).
        ///
        /// On utilise JsonElement car le body mélange string, decimal et DateTime,
        /// ce qui rendrait la désérialisation automatique vers Bond incorrecte.
        /// </summary>
        // POST api/Assets/bonds/from-fmp
        [HttpPost("bonds/from-fmp")]
        public async Task<ActionResult<Asset>> CreateBond([FromBody] JsonElement body)
        {
            // ── Lecture du ticker ─────────────────────────────────────────────
            if (!body.TryGetProperty("ticker", out var tickerEl)
                || string.IsNullOrWhiteSpace(tickerEl.GetString()))
                return BadRequest("Le ticker est requis.");

            string ticker = tickerEl.GetString()!.Trim().ToUpper();

            if (await _context.Asset.AnyAsync(a => a.Ticker == ticker))
                return Conflict($"Un actif avec le ticker '{ticker}' existe déjà.");

            // ── Appel FMP — /stable/profile ───────────────────────────────────
            FmpProfile? profile = await _fmp.GetProfileAsync(ticker);

            if (profile is null)
                return BadRequest(
                    $"Ticker '{ticker}' introuvable sur FMP ou erreur de l'API externe. " +
                    "Vérifiez que le ticker est correct.");

            // ── Appel FMP — /stable/company-notes ────────────────────────────
            // Tente de récupérer CouponRate et MaturityDate.
            // Peut retourner null si FMP ne fournit pas l'information (acceptable).
            FmpBondInfo? bondInfo = await _fmp.GetBondAsync(ticker);

            // ── Lecture des valeurs de secours fournies par le client ─────────
            // Utilisées uniquement si FMP ne retourne pas l'information

            decimal? clientCoupon = null;
            if (body.TryGetProperty("couponRate", out var couponEl)
                && couponEl.ValueKind == JsonValueKind.Number)
                clientCoupon = couponEl.GetDecimal();

            DateTime? clientMaturity = null;
            if (body.TryGetProperty("maturityDate", out var maturityEl)
                && maturityEl.ValueKind == JsonValueKind.String
                && DateTime.TryParse(maturityEl.GetString(), out var parsedDate))
                clientMaturity = parsedDate.Date;

            // ── Construction du Bond ──────────────────────────────────────────
            var bond = new Bond
            {
                Ticker = profile.Symbol,
                Name = profile.Name,
                Currency = profile.Currency,
                Exchange = profile.Exchange,
                CreatedAt = DateTime.UtcNow,

                // CouponRate : FMP en priorité, sinon valeur client, sinon null
                CouponRate = bondInfo?.CouponRate ?? clientCoupon,

                // MaturityDate : FMP en priorité, sinon valeur client, sinon null
                MaturityDate = bondInfo?.MaturityDate ?? clientMaturity
            };

            _context.Asset.Add(bond);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = bond.Id }, bond);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met à jour les métadonnées modifiables d'un actif.
        ///
        /// Champs modifiables : Name, Exchange, Currency.
        /// Champs immuables   : Ticker, CreatedAt (et Id évidemment).
        ///
        /// Le Ticker est immuable car il sert de clé de référence
        /// pour tous les prix historiques importés depuis FMP.
        /// Le changer casserait la correspondance avec les données FMP.
        /// </summary>
        // PUT api/Assets/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Asset input)
        {
            var asset = await _context.Asset.FindAsync(id);

            if (asset is null)
                return NotFound($"Actif {id} introuvable.");

            // ── Name ──────────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(input.Name))
                asset.Name = input.Name.Trim();

            // ── Exchange ──────────────────────────────────────────────────────
            // On accepte une chaîne vide pour effacer la valeur (ex: place inconnue)
            if (input.Exchange is not null)
                asset.Exchange = string.IsNullOrWhiteSpace(input.Exchange)
                    ? null
                    : input.Exchange.Trim();

            // ── Currency ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                string currency = input.Currency.Trim().ToUpper();

                // Code ISO 4217 : exactement 3 lettres majuscules
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
        ///
        /// Bloqué si l'actif est utilisé dans au moins une position.
        ///
        /// Double sécurité :
        ///   1. Vérification applicative ici (message d'erreur clair pour l'utilisateur)
        ///   2. Contrainte Restrict en base (DbContext) — bloque même si on contourne l'API
        ///
        /// La suppression en cascade supprime automatiquement tous les AssetPrice
        /// liés à cet actif (configuré dans le DbContext).
        /// </summary>
        // DELETE api/Assets/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var asset = await _context.Asset.FindAsync(id);

            if (asset is null)
                return NotFound($"Actif {id} introuvable.");

            // Vérifie si l'actif est présent dans au moins une position
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
}
