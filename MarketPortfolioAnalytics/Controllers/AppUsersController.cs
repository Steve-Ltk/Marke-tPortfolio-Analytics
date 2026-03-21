using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace MarketPortfolioAnalytics.Controllers
{
    // Gestion des utilisateurs de la plateforme.
    // Règles importantes :
    //   - On ne supprime JAMAIS un utilisateur en base (soft delete via IsActive).
    //   - Role, CreatedAt et PasswordHash sont toujours imposés par le serveur.
    //   - Le mot de passe est toujours hashé avant stockage (PasswordHasher d'ASP.NET Identity).
    [Route("api/[controller]")]
    [ApiController]
    public class AppUsersController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public AppUsersController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // Retourne tous les utilisateurs ACTIFS uniquement.
        // IsActive = false → compte désactivé, ignoré dans toutes les requêtes.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppUser>>> GetAll()
        {
            return await _context.AppUser
                .Where(u => u.IsActive)
                .ToListAsync();
        }

        // Retourne un utilisateur par son Id, uniquement s'il est actif.
        // "is null" = syntaxe moderne équivalente à "== null".
        [HttpGet("{id}")]
        public async Task<ActionResult<AppUser>> GetById(int id)
        {
            var user = await _context.AppUser
                .FirstOrDefaultAsync(u => u.Id == id && u.IsActive);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable ou inactif.");

            return user;
        }

        // Retourne les portefeuilles d'un utilisateur.
        // Je vérifie d'abord que l'utilisateur existe avant de chercher ses portefeuilles.
        // AnyAsync = plus efficace que FirstOrDefault quand j'ai juste besoin de savoir si ça existe.
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

        // Crée un nouveau compte utilisateur.
        // Ordre important : valider -> vérifier unicité -> construire -> hasher -> sauvegarder.
        // Role = "User" imposé par le serveur -> le client ne peut pas s'auto-proclamer Admin.
        // CreatedAt = DateTime.UtcNow imposé par le serveur -> le client ne choisit pas sa date.
        // Email normalisé en minuscules -> évite les doublons "User@mail.com" vs "user@mail.com".
        // CreatedAtAction -> retourne 201 Created avec l'URL du nouvel utilisateur dans le header.
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

            // Hash le mot de passe avant de sauvegarder
            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            _context.AppUser.Add(user);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        // Met à jour le nom et/ou l'email d'un utilisateur.
        // J'utilise UpdateUserRequest (DTO) et non AppUser directement :
        // AppUser a des [Required] sur plusieurs champs → un PUT partiel serait rejeté en 400
        // avant même d'entrer dans la méthode. Le DTO avec des champs nullable évite ça.
        // StringComparison.OrdinalIgnoreCase → compare les emails sans tenir compte de la casse.
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

        // Change uniquement le rôle (User ou Admin).
        // PATCH = modification partielle, contrairement à PUT qui remplace tout l'objet.
        // Seuls "User" et "Admin" sont acceptés → validation explicite ici.
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

        // Change le mot de passe d'un utilisateur.
        // Je vérifie d'abord l'ANCIEN mot de passe avant d'accepter le nouveau.
        // Sécurité : même si quelqu'un vole la session, il ne peut pas changer le mdp sans le connaître.
        // VerifyHashedPassword -> re-hashe la tentative et compare. Ne déchiffre jamais.
        // NoContent() = 204 : succès mais rien à retourner.
        [HttpPatch("{id}/password")]
        public async Task<IActionResult> UpdatePassword(
        int id, [FromBody] ChangePasswordRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.CurrentPassword))
                return BadRequest("Le mot de passe actuel est requis.");

            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest("Le nouveau mot de passe est requis.");

            if (req.NewPassword.Length < 8)
                return BadRequest("Le nouveau mot de passe doit contenir au moins 8 caractères.");

            var user = await _context.AppUser.FindAsync(id);

            if (user is null)
                return NotFound($"Utilisateur {id} introuvable.");

            if (!user.IsActive)
                return BadRequest("Impossible de modifier le mot de passe d'un utilisateur inactif.");

            //  Vérification du mot de passe actuel
            var hasher = new PasswordHasher<AppUser>();
            var verificationResult = hasher.VerifyHashedPassword(
                user, user.PasswordHash, req.CurrentPassword);

            if (verificationResult == PasswordVerificationResult.Failed)
                return Unauthorized("Mot de passe actuel incorrect.");

            // Mise à jour avec le nouveau mot de passe hashé
            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Réactive un compte désactivé (IsActive = false -> true).
        // Si le compte est déjà actif -> retourne 204 directement, rien à faire.
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

        // Soft delete : met IsActive = false au lieu de supprimer la ligne en base.
        // Si déjà inactif → retourne 204 directement, rien à faire.
        // Les données restent pour l'historique et l'intégrité référentielle.
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

        // Vérifie email + mot de passe et retourne l'utilisateur si valide.
        // Même message d'erreur pour email inconnu ET mauvais mot de passe.
        // Sécurité : un attaquant ne peut pas deviner si un email est enregistré.
        // PasswordHash est [JsonIgnore] → jamais inclus dans la réponse JSON.
        // Retourne 200 + AppUser si valide (PasswordHash est [JsonIgnore]).
        // Retourne 401 si email inconnu, inactif, ou mot de passe incorrect.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email et mot de passe obligatoires.");

            var user = await _context.AppUser
                .FirstOrDefaultAsync(u =>
                    u.Email == req.Email.Trim().ToLower() &&
                    u.IsActive);

            if (user == null)
                return Unauthorized("Email ou mot de passe incorrect.");

            var hasher = new PasswordHasher<AppUser>();
            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);

            if (result == PasswordVerificationResult.Failed)
                return Unauthorized("Email ou mot de passe incorrect.");

            // PasswordHash est [JsonIgnore] dans AppUser → jamais retourné au client
            return Ok(user);
        }
    }


    // DTOs : objets légers pour recevoir les données des requêtes.
    // "record" = classe immutable simplifiée, parfaite pour transporter des données.
    // On n'utilise pas AppUser directement car ses [Required] bloqueraient les mises à jour partielles.

    public record UpdateUserRequest(string? FullName, string? Email);
    public record LoginRequest(string Email, string Password);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
