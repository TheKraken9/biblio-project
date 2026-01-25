using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using biblio_project.Models;
using biblio_project.Services;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

public class AccountController : Controller
{
    private readonly string _connectionString;
    private readonly IPasswordHasher _passwordHasher;

    public AccountController(IConfiguration configuration, IPasswordHasher passwordHasher)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _passwordHasher = passwordHasher;
    }

    // GET: /Account/Login
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // POST: /Account/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Rechercher l'utilisateur par username ou email
        var query = @"
            SELECT Id, Username, Email, PasswordHash, FirstName, LastName, IsActive
            FROM library_user
            WHERE (Username = @UsernameOrEmail OR Email = @UsernameOrEmail)";

        LibraryUser? user = null;
        string passwordHash = string.Empty;

        using (var command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@UsernameOrEmail", model.UsernameOrEmail);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                user = new LibraryUser
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    Email = reader.GetString(2),
                    FirstName = reader.GetString(4),
                    LastName = reader.GetString(5),
                    IsActive = reader.GetBoolean(6)
                };
                passwordHash = reader.GetString(3);
            }
        }

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Nom d'utilisateur ou mot de passe incorrect");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Votre compte est désactivé. Veuillez contacter l'administrateur.");
            return View(model);
        }

        // Vérifier le mot de passe
        if (!_passwordHasher.VerifyPassword(model.Password, passwordHash))
        {
            ModelState.AddModelError(string.Empty, "Nom d'utilisateur ou mot de passe incorrect");
            return View(model);
        }

        // Récupérer les rôles de l'utilisateur
        var roles = await GetUserRolesAsync(connection, user.Id);

        // Créer les claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName)
        };

        // Ajouter les rôles comme claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(2)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "UserDashboard");
    }

    // GET: /Account/Register
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    // POST: /Account/Register
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Vérifier si l'utilisateur existe déjà
        var checkQuery = "SELECT COUNT(*) FROM library_user WHERE Username = @Username OR Email = @Email";
        using (var checkCommand = new SqlCommand(checkQuery, connection))
        {
            checkCommand.Parameters.AddWithValue("@Username", model.Username);
            checkCommand.Parameters.AddWithValue("@Email", model.Email);
            var count = (int)await checkCommand.ExecuteScalarAsync();

            if (count > 0)
            {
                ModelState.AddModelError(string.Empty, "Ce nom d'utilisateur ou cet email est déjà utilisé");
                return View(model);
            }
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            // Hasher le mot de passe
            var passwordHash = _passwordHasher.HashPassword(model.Password);

            // Créer l'utilisateur (Id est auto-généré car c'est un IDENTITY)
            var insertUserQuery = @"
                INSERT INTO library_user (Username, Email, PasswordHash, FirstName, LastName, PhoneNumber, IsActive, CreatedAt, RegistrationDate)
                OUTPUT INSERTED.Id
                VALUES (@Username, @Email, @PasswordHash, @FirstName, @LastName, @PhoneNumber, 1, GETDATE(), GETDATE())";

            int userId;
            using (var command = new SqlCommand(insertUserQuery, connection, transaction))
            {
                command.Parameters.AddWithValue("@Username", model.Username);
                command.Parameters.AddWithValue("@Email", model.Email);
                command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                command.Parameters.AddWithValue("@FirstName", model.FirstName);
                command.Parameters.AddWithValue("@LastName", model.LastName);
                command.Parameters.AddWithValue("@PhoneNumber", (object?)model.PhoneNumber ?? DBNull.Value);

                userId = (int)await command.ExecuteScalarAsync();
            }

            // Attribuer le rôle MEMBER par défaut
            var insertRoleQuery = @"
                INSERT INTO user_role (UserId, RoleId)
                SELECT @UserId, Id FROM role WHERE Name = 'MEMBER'";

            using (var command = new SqlCommand(insertRoleQuery, connection, transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();

            TempData["SuccessMessage"] = "Votre compte a été créé avec succès. Vous pouvez maintenant vous connecter.";
            return RedirectToAction(nameof(Login));
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            ModelState.AddModelError(string.Empty, "Une erreur est survenue lors de la création du compte");
            return View(model);
        }
    }

    // GET: /Account/Logout
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // GET: /Account/ChangePassword
    [HttpGet]
    public IActionResult ChangePassword()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToAction(nameof(Login));
        }

        return View();
    }

    // POST: /Account/ChangePassword
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Récupérer le hash actuel
        var query = "SELECT PasswordHash FROM library_user WHERE Id = @UserId";
        string currentPasswordHash;

        using (var command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            currentPasswordHash = (string)await command.ExecuteScalarAsync();
        }

        // Vérifier le mot de passe actuel
        if (!_passwordHasher.VerifyPassword(model.CurrentPassword, currentPasswordHash))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Le mot de passe actuel est incorrect");
            return View(model);
        }

        // Hasher le nouveau mot de passe
        var newPasswordHash = _passwordHasher.HashPassword(model.NewPassword);

        // Mettre à jour le mot de passe (note: library_user n'a pas de colonne UpdatedAt selon le schéma)
        var updateQuery = "UPDATE library_user SET PasswordHash = @PasswordHash WHERE Id = @UserId";
        using (var command = new SqlCommand(updateQuery, connection))
        {
            command.Parameters.AddWithValue("@PasswordHash", newPasswordHash);
            command.Parameters.AddWithValue("@UserId", userId);
            await command.ExecuteNonQueryAsync();
        }

        TempData["SuccessMessage"] = "Votre mot de passe a été modifié avec succès";
        return RedirectToAction("Index", "UserDashboard");
    }

    // GET: /Account/AccessDenied
    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task<List<string>> GetUserRolesAsync(SqlConnection connection, int userId)
    {
        var roles = new List<string>();
        var query = @"
            SELECT r.Name
            FROM role r
            INNER JOIN user_role ur ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));
        }

        return roles;
    }
}
