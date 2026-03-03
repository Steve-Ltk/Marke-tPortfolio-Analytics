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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppUser>>> GetAll()
        {
            return await _context.AppUser
                .Where(u => u.IsActive)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AppUser>> GetById(int id)
        {
            var user = await _context.AppUser
                .FirstOrDefaultAsync(u => u.Id == id && u.IsActive);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable ou inactif.");

            return user;
        }

        [HttpGet("{id}/portfolios")]
        public async Task<ActionResult<IEnumerable<Portfolio>>> GetPortfolios(int id)
        {
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

        [HttpPost]
        public async Task<ActionResult<AppUser>> Create([FromBody] AppUser input)
        {
            if (string.IsNullOrWhiteSpace(input.FullName))
                return BadRequest("Le nom complet est requis.");

            if (string.IsNullOrWhiteSpace(input.Email))
                return BadRequest("L'email est requis.");

            if (!new EmailAddressAttribute().IsValid(input.Email))
                return BadRequest("Le format de l'email est invalide.");

            if (string.IsNullOrWhiteSpace(input.Password))
                return BadRequest("Le mot de passe est requis.");

            if (input.Password.Length < 8)
                return BadRequest("Le mot de passe doit contenir au moins 8 caractères.");

            string normalizedEmail = input.Email.Trim().ToLower();

            bool emailTaken = await _context.AppUser
                .AnyAsync(u => u.Email == normalizedEmail);

            if (emailTaken)
                return Conflict("Un compte existe déjà avec cet email.");

            var user = new AppUser
            {
                FullName = input.FullName?.Trim() ?? string.Empty,
                Email = normalizedEmail,
                Role = "User",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            _context.AppUser.Add(user);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MISE À JOUR
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met à jour FullName et/ou Email d'un utilisateur actif.
        ///
        /// IMPORTANT : on accepte un DTO léger (UpdateUserRequest) et non le modèle
        /// AppUser complet, car AppUser a [Required] sur Email, Role, IsActive et CreatedAt.
        /// Un PUT avec seulement {"fullName": "Alice"} déclencherait sinon une erreur
        /// de validation 400 avant d'atteindre la méthode.
        /// </summary>
        // PUT api/AppUsers/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest input)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (!user.IsActive)
                return BadRequest("Impossible de modifier un utilisateur inactif.");

            if (!string.IsNullOrWhiteSpace(input.FullName))
                user.FullName = input.FullName.Trim();

            if (!string.IsNullOrWhiteSpace(input.Email))
            {
                if (!new EmailAddressAttribute().IsValid(input.Email))
                    return BadRequest("Le format de l'email est invalide.");

                string normalized = input.Email.Trim().ToLower();

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
            return NoContent();
        }

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

        [HttpPatch("{id}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (user.IsActive)
                return NoContent();

            user.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SUPPRESSION (soft delete)
        // ═══════════════════════════════════════════════════════════════════════

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (!user.IsActive)
                return NoContent();

            user.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DTO — évite la validation prématurée du modèle AppUser complet
    //
    // Pourquoi un DTO ?
    //   AppUser a [Required] sur Email, Role, IsActive et CreatedAt.
    //   Lors d'un PUT avec {"fullName": "Alice"}, ASP.NET rejette en 400 avant
    //   d'appeler la méthode. Avec UpdateUserRequest (champs nullable), la
    //   validation du modèle passe et le contrôleur gère la logique métier.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de mise à jour d'un utilisateur (PUT).</summary>
    public record UpdateUserRequest(string? FullName, string? Email);
}