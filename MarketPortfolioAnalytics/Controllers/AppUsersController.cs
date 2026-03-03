using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;

namespace MarketPortfolioAnalytics.Controllers
{
    /// <summary>
    /// Gestion des utilisateurs de la plateforme.
    ///
    /// Règles importantes :
    ///   - On ne supprime JAMAIS un utilisateur en base (soft delete via IsActive).
    ///   - Role, CreatedAt et PasswordHash sont toujours imposés par le serveur.
    ///   - Le mot de passe est toujours hashé avant stockage (PasswordHasher d'ASP.NET Identity).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AppUsersController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public AppUsersController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne la liste de tous les utilisateurs actifs.
        /// Les utilisateurs désactivés (soft delete) ne sont pas retournés.
        /// </summary>
        // GET api/AppUsers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppUser>>> GetAll()
        {
            return await _context.AppUser
                .Where(u => u.IsActive)
                .ToListAsync();
        }

        /// <summary>
        /// Retourne un utilisateur actif par son Id.
        /// Retourne 404 si l'utilisateur n'existe pas ou est désactivé.
        /// </summary>
        // GET api/AppUsers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AppUser>> GetById(int id)
        {
            var user = await _context.AppUser
                .FirstOrDefaultAsync(u => u.Id == id && u.IsActive);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable ou inactif.");

            return user;
        }

        /// <summary>
        /// Retourne tous les portefeuilles appartenant à un utilisateur actif.
        /// </summary>
        // GET api/AppUsers/5/portfolios
        [HttpGet("{id}/portfolios")]
        public async Task<ActionResult<IEnumerable<Portfolio>>> GetPortfolios(int id)
        {
            // Vérification que l'utilisateur existe et est actif
            bool userExists = await _context.AppUser
                .AnyAsync(u => u.Id == id && u.IsActive);

            if (!userExists)
                return NotFound($"Utilisateur {id} introuvable ou inactif.");

            var portfolios = await _context.Portfolio
                .Where(p => p.UserId == id)
                .ToListAsync();

            return portfolios;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CRÉATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Crée un nouvel utilisateur.
        ///
        /// Champs attendus dans le body JSON :
        ///   - FullName    : nom complet (requis)
        ///   - Email       : email valide et unique (requis)
        ///   - Password    : mot de passe en clair, min 8 caractères (requis)
        ///
        /// Champs imposés par le serveur (ignorés si fournis par le client) :
        ///   - Role        → "User" par défaut
        ///   - IsActive    → true
        ///   - CreatedAt   → DateTime.UtcNow
        ///   - PasswordHash → calculé ici à partir de Password
        /// </summary>
        // POST api/AppUsers
        [HttpPost]
        public async Task<ActionResult<AppUser>> Create([FromBody] AppUser input)
        {
            // ── Validation email ──────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(input.FullName))
                return BadRequest("Le nom complet est requis.");

            if (string.IsNullOrWhiteSpace(input.Email))
                return BadRequest("L'email est requis.");

            if (!new EmailAddressAttribute().IsValid(input.Email))
                return BadRequest("Le format de l'email est invalide.");

            // ── Validation mot de passe ───────────────────────────────────────
            if (string.IsNullOrWhiteSpace(input.Password))
                return BadRequest("Le mot de passe est requis.");

            if (input.Password.Length < 8)
                return BadRequest("Le mot de passe doit contenir au moins 8 caractères.");

            // ── Unicité de l'email ────────────────────────────────────────────
            // On normalise en minuscules pour éviter les doublons "User@example.com" vs "user@example.com"
            string normalizedEmail = input.Email.Trim().ToLower();

            bool emailTaken = await _context.AppUser
                .AnyAsync(u => u.Email == normalizedEmail);

            if (emailTaken)
                return Conflict("Un compte existe déjà avec cet email.");

            // ── Construction de l'entité ──────────────────────────────────────
            // On construit un nouvel objet au lieu de modifier input directement.
            // Cela garantit que le client ne peut pas imposer Role, IsActive ou CreatedAt.
            var user = new AppUser
            {
                FullName = input.FullName?.Trim() ?? string.Empty,
                Email = normalizedEmail,
                Role = "User",       // toujours "User" à la création
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Hash du mot de passe via ASP.NET Identity
            // PasswordHasher utilise PBKDF2 avec sel aléatoire — sécurisé
            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            _context.AppUser.Add(user);
            await _context.SaveChangesAsync();

            // 201 Created avec l'URL de la ressource créée dans le header Location
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met à jour FullName et/ou Email d'un utilisateur actif.
        ///
        /// Champs modifiables : FullName, Email.
        /// Champs ignorés même si fournis : Role, IsActive, CreatedAt, Password.
        /// </summary>
        // PUT api/AppUsers/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] AppUser input)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (!user.IsActive)
                return BadRequest("Impossible de modifier un utilisateur inactif.");

            // ── FullName ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(input.FullName))
                user.FullName = input.FullName.Trim();

            // ── Email ─────────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(input.Email))
            {
                if (!new EmailAddressAttribute().IsValid(input.Email))
                    return BadRequest("Le format de l'email est invalide.");

                string normalized = input.Email.Trim().ToLower();

                // On vérifie l'unicité uniquement si l'email change réellement
                if (!string.Equals(user.Email, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    bool emailTaken = await _context.AppUser
                        .AnyAsync(u => u.Email == normalized);

                    if (emailTaken)
                        return Conflict("Un compte existe déjà avec cet email.");

                    user.Email = normalized;
                }
            }

            await _context.SaveChangesAsync();
            return NoContent(); // 204 : succès sans contenu retourné
        }

        /// <summary>
        /// Change le rôle d'un utilisateur.
        /// Valeurs acceptées : "User" ou "Admin".
        ///
        /// Route dédiée pour éviter qu'un simple PUT puisse modifier le rôle.
        /// </summary>
        // PATCH api/AppUsers/5/role
        [HttpPatch("{id}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return BadRequest("Le rôle est requis.");

            role = role.Trim();

            if (role != "User" && role != "Admin")
                return BadRequest("Le rôle doit être 'User' ou 'Admin'.");

            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            user.Role = role;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Change le mot de passe d'un utilisateur actif.
        /// Le nouveau mot de passe est hashé avant stockage.
        /// </summary>
        // PATCH api/AppUsers/5/password
        [HttpPatch("{id}/password")]
        public async Task<IActionResult> UpdatePassword(int id, [FromBody] string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return BadRequest("Le mot de passe est requis.");

            if (newPassword.Length < 8)
                return BadRequest("Le mot de passe doit contenir au moins 8 caractères.");

            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (!user.IsActive)
                return BadRequest("Impossible de modifier le mot de passe d'un utilisateur inactif.");

            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, newPassword);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Réactive un compte utilisateur désactivé.
        /// Sans effet si le compte est déjà actif (idempotent).
        /// </summary>
        // PATCH api/AppUsers/5/activate
        [HttpPatch("{id}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            // Déjà actif → on retourne 204 sans rien faire (idempotent)
            if (user.IsActive)
                return NoContent();

            user.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION (soft delete)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Désactive un utilisateur (soft delete).
        /// L'enregistrement reste en base — IsActive passe à false.
        /// Sans effet si le compte est déjà inactif (idempotent).
        ///
        /// On ne supprime jamais un utilisateur en base car :
        ///   - ses portefeuilles et positions constituent un historique
        ///   - la contrainte Restrict empêcherait la suppression si des portfolios existent
        /// </summary>
        // DELETE api/AppUsers/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            // Déjà inactif → idempotent
            if (!user.IsActive)
                return NoContent();

            user.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
