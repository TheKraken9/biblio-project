using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using biblio_project.Models;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

[Authorize] // Nécessite l'authentification
public class UserDashboardController : Controller
{
    private readonly string _connectionString;

    public UserDashboardController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string not found");
    }

    public async Task<IActionResult> Index()
    {
        // Récupérer l'ID de l'utilisateur connecté
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return RedirectToAction("Login", "Account");
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var user = await GetUserAsync(connection, userId);
        if (user == null)
        {
            return NotFound();
        }

        var viewModel = new UserDashboardViewModel
        {
            User = user,
            CurrentLoans = await GetUserLoansAsync(connection, userId),
            ActiveReservations = await GetUserReservationsAsync(connection, userId),
            MaxConcurrentLoans = await GetAppSettingIntAsync(connection, "MAX_CONCURRENT_LOANS", 5),
            MaxReservations = await GetAppSettingIntAsync(connection, "MAX_RESERVATIONS", 3)
        };

        return View(viewModel);
    }

    // Reste des méthodes privées inchangées...
    private async Task<LibraryUser?> GetUserAsync(SqlConnection connection, Guid userId)
    {
        var query = @"
            SELECT Id, Username, Email, FirstName, LastName, PhoneNumber, 
                   IsActive, RegistrationDate
            FROM LibraryUser
            WHERE Id = @UserId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new LibraryUser
            {
                Id = reader.GetGuid(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2),
                FirstName = reader.GetString(3),
                LastName = reader.GetString(4),
                PhoneNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsActive = reader.GetBoolean(6),
                RegistrationDate = reader.GetDateTime(7)
            };
        }

        return null;
    }

    private async Task<List<Loan>> GetUserLoansAsync(SqlConnection connection, Guid userId)
    {
        var loans = new List<Loan>();
        var query = @"
            SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate,
                   Status, RenewalCount, BookId, BookTitleSnapshot
            FROM Loan
            WHERE BorrowerId = @UserId AND Status = 'ONGOING'
            ORDER BY DueDate";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            loans.Add(new Loan
            {
                Id = reader.GetInt32(0),
                BookCopyId = reader.GetInt32(1),
                BorrowerId = reader.GetGuid(2),
                LoanDate = reader.GetDateTime(3),
                DueDate = reader.GetDateTime(4),
                ReturnDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                Status = reader.GetString(6),
                RenewalCount = reader.GetInt32(7),
                BookId = reader.GetInt32(8),
                BookTitleSnapshot = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return loans;
    }

    private async Task<List<Reservation>> GetUserReservationsAsync(SqlConnection connection, Guid userId)
    {
        var reservations = new List<Reservation>();
        var query = @"
            SELECT Id, BookId, RequesterId, AssignedCopyId, Status, PositionInQueue,
                   RequestedAt, ExpiresAt, BookTitleSnapshot
            FROM Reservation
            WHERE RequesterId = @UserId AND Status IN ('PENDING', 'READY_FOR_PICKUP')
            ORDER BY RequestedAt";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            reservations.Add(new Reservation
            {
                Id = reader.GetInt32(0),
                BookId = reader.GetInt32(1),
                RequesterId = reader.GetGuid(2),
                AssignedCopyId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Status = reader.GetString(4),
                PositionInQueue = reader.GetInt32(5),
                RequestedAt = reader.GetDateTime(6),
                ExpiresAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                BookTitleSnapshot = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return reservations;
    }

    private async Task<int> GetAppSettingIntAsync(SqlConnection connection, string key, int defaultValue)
    {
        var query = "SELECT [Value] FROM AppSetting WHERE [Key] = @Key";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Key", key);
        
        var result = await command.ExecuteScalarAsync();
        if (result != null && int.TryParse(result.ToString(), out int value))
        {
            return value;
        }

        return defaultValue;
    }
}