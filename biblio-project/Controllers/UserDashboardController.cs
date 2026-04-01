using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using biblio_project.Models;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

[Authorize]
public class UserDashboardController : Controller
{
    private readonly string _connectionString;
    private readonly int _maxConcurrentLoans = 5;
    private readonly int _maxReservations = 3;

    public UserDashboardController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");

        // Charger les paramètres depuis la configuration
        var librarySettings = configuration.GetSection("LibrarySettings");
        if (librarySettings.Exists())
        {
            _maxConcurrentLoans = librarySettings.GetValue<int>("MaxConcurrentLoans", 5);
            _maxReservations = librarySettings.GetValue<int>("MaxReservations", 3);
        }
    }

    public async Task<IActionResult> Index()
    {
        // Récupérer l'ID de l'utilisateur connecté
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
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
            PendingLoans = await GetUserPendingLoansAsync(connection, userId),
            ActiveReservations = await GetUserReservationsAsync(connection, userId),
            MaxConcurrentLoans = _maxConcurrentLoans,
            MaxReservations = _maxReservations
        };

        return View(viewModel);
    }

    private async Task<LibraryUser?> GetUserAsync(SqlConnection connection, int userId)
    {
        var query = @"
            SELECT Id, Username, Email, FirstName, LastName, PhoneNumber,
                   IsActive, RegistrationDate
            FROM library_user
            WHERE Id = @UserId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new LibraryUser
            {
                Id = reader.GetInt32(0),
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

    private async Task<List<Loan>> GetUserLoansAsync(SqlConnection connection, int userId)
    {
        var loans = new List<Loan>();
        var query = @"
            SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate,
                   Status, RenewalCount, BookId, BookTitleSnapshot
            FROM Loans
            WHERE BorrowerId = @UserId AND Status = 'onLoan'
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
                BorrowerId = reader.GetInt32(2),
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

    private async Task<List<Loan>> GetUserPendingLoansAsync(SqlConnection connection, int userId)
    {
        var loans = new List<Loan>();
        var query = @"
            SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate,
                   Status, RenewalCount, BookId, BookTitleSnapshot
            FROM Loans
            WHERE BorrowerId = @UserId AND Status = 'reserved'
            ORDER BY LoanDate DESC";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            loans.Add(new Loan
            {
                Id = reader.GetInt32(0),
                BookCopyId = reader.GetInt32(1),
                BorrowerId = reader.GetInt32(2),
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

    private async Task<List<Reservation>> GetUserReservationsAsync(SqlConnection connection, int userId)
    {
        var reservations = new List<Reservation>();
        var query = @"
            SELECT Id, BookId, RequesterId, Status, PositionInQueue,
                   RequestedAt, ExpireAt, BookTitleSnapshot
            FROM Reservations
            WHERE RequesterId = @UserId AND Status IN ('Pending', 'ReadyForPickup')
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
                RequesterId = reader.GetInt32(2),
                Status = reader.GetString(3),
                PositionInQueue = reader.GetInt32(4),
                RequestedAt = reader.GetDateTime(5),
                ExpiresAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                BookTitleSnapshot = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return reservations;
    }
}
