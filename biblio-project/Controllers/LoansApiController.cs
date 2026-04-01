using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

[ApiController]
[Route("api/loans")]
public class LoansApiController : ControllerBase
{
    private readonly string _connectionString;
    private readonly int _defaultLoanDurationDays;
    private readonly int _maxConcurrentLoans;
    private readonly int _maxRenewals;
    private readonly int _renewalExtensionDays;

    public LoansApiController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _defaultLoanDurationDays = configuration.GetValue<int>("LibrarySettings:DefaultLoanDurationDays", 14);
        _maxConcurrentLoans = configuration.GetValue<int>("LibrarySettings:MaxConcurrentLoans", 5);
        _maxRenewals = configuration.GetValue<int>("LibrarySettings:MaxRenewals", 2);
        _renewalExtensionDays = 7; // Extension accordée par renouvellement
    }

    // ── Emprunter un livre ────────────────────────────────────────────────────
    [HttpPost("borrow")]
    public async Task<ActionResult<ApiResponse<Loan>>> BorrowBook([FromBody] BorrowBookRequest request)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (!await UserExistsAsync(connection, request.UserId))
                return BadRequest(ApiResponse<Loan>.Fail("Utilisateur non trouvé"));

            var currentLoansCount = await GetUserActiveLoansCountAsync(connection, request.UserId);
            if (currentLoansCount >= _maxConcurrentLoans)
                return BadRequest(ApiResponse<Loan>.Fail(
                    $"Limite de {_maxConcurrentLoans} emprunts simultanés atteinte"));

            var availableCopy = await GetAvailableBookCopyAsync(connection, request.BookId);
            if (availableCopy == null)
                return BadRequest(ApiResponse<Loan>.Fail("Aucun exemplaire disponible pour ce livre"));

            if (await HasActiveLoanForBookAsync(connection, request.UserId, request.BookId))
                return BadRequest(ApiResponse<Loan>.Fail("Vous avez déjà emprunté ce livre"));

            var bookInfo = await GetBookInfoAsync(connection, request.BookId);
            var userInfo = await GetUserInfoAsync(connection, request.UserId);

            if (bookInfo == null || userInfo == null)
                return BadRequest(ApiResponse<Loan>.Fail("Données invalides"));

            using var transaction = connection.BeginTransaction();
            try
            {
                var loanDate = DateTime.Now;
                var dueDate = loanDate.AddDays(_defaultLoanDurationDays);

                var insertLoanQuery = @"
                    INSERT INTO Loan (BookCopyId, BorrowerId, LoanDate, DueDate, Status, BookId,
                                     BookTitleSnapshot, BorrowerNameSnapshot, BorrowerEmailSnapshot)
                    OUTPUT INSERTED.Id
                    VALUES (@BookCopyId, @BorrowerId, @LoanDate, @DueDate, 'ONGOING', @BookId,
                            @BookTitle, @BorrowerName, @BorrowerEmail)";

                int loanId;
                using (var command = new SqlCommand(insertLoanQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@BookCopyId", availableCopy.Id);
                    command.Parameters.AddWithValue("@BorrowerId", request.UserId);
                    command.Parameters.AddWithValue("@LoanDate", loanDate);
                    command.Parameters.AddWithValue("@DueDate", dueDate);
                    command.Parameters.AddWithValue("@BookId", request.BookId);
                    command.Parameters.AddWithValue("@BookTitle", bookInfo.Title);
                    command.Parameters.AddWithValue("@BorrowerName", userInfo.FullName);
                    command.Parameters.AddWithValue("@BorrowerEmail", userInfo.Email);
                    loanId = (int)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException());
                }

                var updateCopyQuery = "UPDATE BookCopy SET Status = 'ON_LOAN' WHERE Id = @CopyId";
                using (var command = new SqlCommand(updateCopyQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@CopyId", availableCopy.Id);
                    await command.ExecuteNonQueryAsync();
                }

                var updateBookQuery = @"
                    UPDATE Book SET AvailableCopiesCount = AvailableCopiesCount - 1 WHERE Id = @BookId";
                using (var command = new SqlCommand(updateBookQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@BookId", request.BookId);
                    await command.ExecuteNonQueryAsync();
                }

                await UpdateBookStatisticsAsync(connection, transaction, request.BookId);
                transaction.Commit();

                return Ok(ApiResponse<Loan>.Ok(new Loan
                {
                    Id = loanId,
                    BookCopyId = availableCopy.Id,
                    BorrowerId = request.UserId,
                    LoanDate = loanDate,
                    DueDate = dueDate,
                    Status = "ONGOING",
                    BookId = request.BookId,
                    BookTitleSnapshot = bookInfo.Title
                }, "Livre emprunté avec succès"));
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<Loan>.Fail("Erreur lors de l'emprunt du livre"));
        }
    }

    // ── Renouveler un emprunt ─────────────────────────────────────────────────
    /// <summary>
    /// Fonctionnalité complexe : renouvellement avec contrôles métier multiples.
    /// Vérifie : emprunt actif, propriétaire, limite de renouvellements, pas de réservation
    /// en attente par un autre utilisateur sur le même livre.
    /// </summary>
    [HttpPost("{loanId}/renew")]
    public async Task<ActionResult<ApiResponse<Loan>>> RenewLoan(int loanId, [FromQuery] int userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Récupérer l'emprunt
            var loan = await GetLoanAsync(connection, loanId);

            if (loan == null)
                return NotFound(ApiResponse<Loan>.Fail("Emprunt non trouvé"));

            if (loan.BorrowerId != userId)
                return Forbid();

            if (loan.Status != "ONGOING")
                return BadRequest(ApiResponse<Loan>.Fail("Cet emprunt n'est plus actif"));

            if (loan.RenewalCount >= _maxRenewals)
                return BadRequest(ApiResponse<Loan>.Fail(
                    $"Limite de {_maxRenewals} renouvellement(s) atteinte pour cet emprunt"));

            // Vérifier qu'aucun autre utilisateur n'attend ce livre en réservation
            var pendingReservations = await GetPendingReservationsCountAsync(connection, loan.BookId);
            if (pendingReservations > 0)
                return BadRequest(ApiResponse<Loan>.Fail(
                    "Ce livre est réservé par d'autres membres. Le renouvellement n'est pas possible."));

            // Appliquer le renouvellement
            using var transaction = connection.BeginTransaction();
            try
            {
                var newDueDate = loan.DueDate.AddDays(_renewalExtensionDays);

                var updateQuery = @"
                    UPDATE Loan
                    SET DueDate = @NewDueDate,
                        RenewalCount = RenewalCount + 1
                    WHERE Id = @LoanId";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@NewDueDate", newDueDate);
                    command.Parameters.AddWithValue("@LoanId", loanId);
                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();

                loan.DueDate = newDueDate;
                loan.RenewalCount++;

                return Ok(ApiResponse<Loan>.Ok(loan,
                    $"Emprunt renouvelé avec succès. Nouvelle date de retour : {newDueDate:dd/MM/yyyy}"));
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<Loan>.Fail("Erreur lors du renouvellement"));
        }
    }

    // ── Emprunts d'un utilisateur ─────────────────────────────────────────────
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<Loan>>>> GetUserLoans(int userId, [FromQuery] bool activeOnly = true)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var statusFilter = activeOnly ? "AND Status = 'ONGOING'" : "";
            var query = $@"
                SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate,
                       Status, RenewalCount, BookId, BookTitleSnapshot
                FROM Loan
                WHERE BorrowerId = @UserId {statusFilter}
                ORDER BY LoanDate DESC";

            var loans = new List<Loan>();
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

            return Ok(ApiResponse<List<Loan>>.Ok(loans, $"{loans.Count} emprunt(s) trouvé(s)"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<List<Loan>>.Fail("Erreur lors de la récupération des emprunts"));
        }
    }

    // ── Méthodes privées ──────────────────────────────────────────────────────

    private async Task<Loan?> GetLoanAsync(SqlConnection connection, int loanId)
    {
        var query = @"
            SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate,
                   Status, RenewalCount, BookId, BookTitleSnapshot
            FROM Loan WHERE Id = @LoanId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@LoanId", loanId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new Loan
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
            };
        }

        return null;
    }

    private async Task<int> GetPendingReservationsCountAsync(SqlConnection connection, int bookId)
    {
        var query = "SELECT COUNT(*) FROM Reservation WHERE BookId = @BookId AND Status = 'PENDING'";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private async Task<bool> UserExistsAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT COUNT(*) FROM LibraryUser WHERE Id = @UserId AND IsActive = 1";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return (int)(await command.ExecuteScalarAsync() ?? 0) > 0;
    }

    private async Task<int> GetUserActiveLoansCountAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT COUNT(*) FROM Loan WHERE BorrowerId = @UserId AND Status = 'ONGOING'";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private async Task<BookCopy?> GetAvailableBookCopyAsync(SqlConnection connection, int bookId)
    {
        var query = @"
            SELECT TOP 1 Id, BookId, Barcode, ShelfLocation, Condition, Status
            FROM BookCopy
            WHERE BookId = @BookId AND Status = 'AVAILABLE' AND IsReferenceOnly = 0
            ORDER BY AcquisitionDate";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new BookCopy
            {
                Id = reader.GetInt32(0),
                BookId = reader.GetInt32(1),
                Barcode = reader.GetString(2),
                ShelfLocation = reader.GetString(3),
                Condition = reader.GetString(4),
                Status = reader.GetString(5)
            };
        }

        return null;
    }

    private async Task<bool> HasActiveLoanForBookAsync(SqlConnection connection, int userId, int bookId)
    {
        var query = @"
            SELECT COUNT(*) FROM Loan
            WHERE BorrowerId = @UserId AND BookId = @BookId AND Status = 'ONGOING'";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@BookId", bookId);
        return (int)(await command.ExecuteScalarAsync() ?? 0) > 0;
    }

    private async Task<Book?> GetBookInfoAsync(SqlConnection connection, int bookId)
    {
        var query = "SELECT Id, Title FROM Book WHERE Id = @BookId";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
            return new Book { Id = reader.GetInt32(0), Title = reader.GetString(1) };

        return null;
    }

    private async Task<LibraryUser?> GetUserInfoAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT Id, FirstName, LastName, Email FROM LibraryUser WHERE Id = @UserId";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new LibraryUser
            {
                Id = reader.GetInt32(0),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                Email = reader.GetString(3)
            };
        }

        return null;
    }

    private async Task UpdateBookStatisticsAsync(SqlConnection connection, SqlTransaction transaction, int bookId)
    {
        var query = @"
            IF EXISTS (SELECT 1 FROM BookStatistics WHERE BookId = @BookId)
            BEGIN
                UPDATE BookStatistics
                SET TotalLoansCount = TotalLoansCount + 1,
                    CurrentActiveLoansCount = (SELECT COUNT(*) FROM Loan WHERE BookId = @BookId AND Status = 'ONGOING'),
                    LastLoanDate = GETDATE(),
                    UpdatedAt = GETDATE()
                WHERE BookId = @BookId
            END
            ELSE
            BEGIN
                INSERT INTO BookStatistics (BookId, TotalLoansCount, CurrentActiveLoansCount, LastLoanDate)
                VALUES (@BookId, 1, 1, GETDATE())
            END";

        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        await command.ExecuteNonQueryAsync();
    }
}

public class BorrowBookRequest
{
    public int BookId { get; set; }
    public int UserId { get; set; }
}
