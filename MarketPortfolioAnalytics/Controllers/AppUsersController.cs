using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Identity;

namespace MarketPortfolioAnalytics.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppUsersController : ControllerBase
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public AppUsersController(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // GET: api/AppUsers
        // Retourne seulement les utilisateurs actifs
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AppUser>>> GetAppUser()
        {
            return await _context.AppUser
                .Where(u => u.IsActive)
                .ToListAsync();
        }

        // GET: api/AppUsers/5
        // Ne retourne pas un utilisateur inactif
        [HttpGet("{id}")]
        public async Task<ActionResult<AppUser>> GetAppUser(int id)
        {
            var appUser = await _context.AppUser
                .FirstOrDefaultAsync(u => u.Id == id && u.IsActive);

            if (appUser == null)
                return NotFound();

            return appUser;
        }


        // PUT: api/AppUsers/5
        // Mise à jour contrôlée : le client ne peut pas modifier Role / CreatedAt / IsActive
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAppUser(int id, AppUser input)
        {
            var user = await _context.AppUser.FindAsync(id);
            if (user == null) return NotFound();

            // Option: empêche de modifier un user déjà désactivé
            if (!user.IsActive) return BadRequest("User is inactive.");

            // Email obligatoire
            if (string.IsNullOrWhiteSpace(input.Email))
                return BadRequest("Email is required.");

            // Email format OK
            var emailValidator = new EmailAddressAttribute();
            if (!emailValidator.IsValid(input.Email))
                return BadRequest("Email format is invalid.");

            // Email unique si l'email change
            if (!string.Equals(user.Email, input.Email, StringComparison.OrdinalIgnoreCase))
            {
                bool emailExists = await _context.AppUser.AnyAsync(u => u.Email == input.Email);
                if (emailExists)
                    return BadRequest("Email already exists.");

                user.Email = input.Email;
            }

            // Champs autorisés
            user.FullName = input.FullName;

            // Champs sensibles ignorés (même si le client les envoie)
            // user.Role = user.Role;
            // user.CreatedAt = user.CreatedAt;
            // user.IsActive = user.IsActive;

            await _context.SaveChangesAsync();
            return NoContent();
        }


        // GET: api/AppUsers/5/portfolios
        [HttpGet("{id}/portfolios")]
        public async Task<ActionResult<IEnumerable<Portfolio>>> GetUserPortfolios(int id)
        {
            // 1) Vérifie que l'utilisateur existe ET est actif
            var userExists = await _context.AppUser.AnyAsync(u => u.Id == id && u.IsActive);
            if (!userExists) return NotFound("User not found or inactive.");

            // 2) Retourne ses portefeuilles
            var portfolios = await _context.Portfolio
                .Where(p => p.UserId == id)
                .ToListAsync();

            return portfolios;
        }


        // POST: api/AppUsers
        // Création contrôlée : Role/CreatedAt/IsActive imposés par le serveur
        [HttpPost]
        public async Task<ActionResult<AppUser>> PostAppUser(AppUser input)
        {
            if (string.IsNullOrWhiteSpace(input.Email))
                return BadRequest("Email is required.");

            var emailValidator = new EmailAddressAttribute();
            if (!emailValidator.IsValid(input.Email))
                return BadRequest("Email format is invalid.");

            if (string.IsNullOrWhiteSpace(input.Password))
                return BadRequest("Password is required.");

            if (input.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var normalizedEmail = input.Email.Trim().ToLower();

            bool emailExists = await _context.AppUser
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail);

            if (emailExists)
                return BadRequest("Email already exists.");

            var user = new AppUser
            {
                FullName = input.FullName,
                Email = normalizedEmail,
                Role = "User",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Hash du mot de passe
            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            _context.AppUser.Add(user);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAppUser), new { id = user.Id }, user);
        }



        // DELETE: api/AppUsers/5
        // Soft delete: on désactive au lieu de supprimer
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAppUser(int id)
        {
            var user = await _context.AppUser.FindAsync(id);
            if (user == null) return NotFound();

            if (!user.IsActive) return NoContent(); // déjà désactivé

            user.IsActive = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }


        // PATCH: api/AppUsers/5/activate
        [HttpPatch("{id}/activate")]
        public async Task<IActionResult> ActivateUser(int id)
        {
            var user = await _context.AppUser.FindAsync(id);
            if (user == null)
                return NotFound();

            if (user.IsActive)
                return NoContent();

            user.IsActive = true;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // PATCH: api/AppUsers/5/role
        [HttpPatch("{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return BadRequest("Role is required.");

            role = role.Trim();

            if (role != "User" && role != "Admin")
                return BadRequest("Role must be 'User' or 'Admin'.");

            var user = await _context.AppUser.FindAsync(id);
            if (user == null)
                return NotFound();

            user.Role = role;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // PATCH: api/AppUsers/5/password
        [HttpPatch("{id}/password")]
        public async Task<IActionResult> UpdatePassword(int id, [FromBody] string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return BadRequest("Password is required.");

            if (newPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var user = await _context.AppUser.FindAsync(id);
            if (user == null) return NotFound();
            if (!user.IsActive) return BadRequest("User is inactive.");

            var hasher = new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, newPassword);
            user.PasswordUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }


    }
}
