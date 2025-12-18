using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using System.Data.SqlClient;
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

    public LoansApiController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string not found");
        _defaultLoanDurationDays = 14; // Par défaut
        _maxConcurrentLoans = 5;
        _maxRenewals = 2;
    }

    [HttpPost("borrow")]
    public async Task<ActionResult<ApiResponse<Loan>>> BorrowBook([FromBody] BorrowBookRequest request)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Vérifier que l'utilisateur existe
            if (!await UserExistsAsync(connection, request.UserId))
            {
                return BadRequest(new ApiResponse<Loan>
                {
                    Success = false,
                    Message = "Utilisateur non trouvé"
                });
            }

            // Vérifier le nombre d'emprunts actuels
            var currentLoansCount = await GetUserActiveLoansCountAsync(connection, request.UserId);
            if (currentLoansCount >= _maxConcurrentLoans)
            {
                return BadRequest(new ApiResponse<Loan>
                {
                    Success = false,
                    Message = $"Vous avez atteint la limite de {_maxConcurrentLoans} emprunts simultanés"
                });
            }

            // Vérifier qu'un exemplaire est disponible
            var availableCopy = await GetAvailableBookCopyAsync(connection, request.BookId);
            if (availableCopy == null)
            {
                return BadRequest(new ApiResponse<Loan>
                {
                    Success = false,
                    Message = "Aucun exemplaire disponible pour ce livre"
                });
            }

            // Vérifier si l'utilisateur n'a pas déjà emprunté ce livre
            if (await HasActiveLoanForBookAsync(connection, request.UserId, request.BookId))
            {
                return BadRequest(new ApiResponse<Loan>
                {
                    Success = false,
                    Message = "Vous avez déjà emprunté ce livre"
                });
            }

            // Récupérer les informations du livre et de l'utilisateur pour les snapshots
            var bookInfo = await GetBookInfoAsync(connection, request.BookId);
            var userInfo = await GetUserInfoAsync(connection, request.UserId);

            if (bookInfo == null || userInfo == null)
            {
                return BadRequest(new ApiResponse<Loan>
                {
                    Success = false,
                    Message = "Données invalides"
                });
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                // Créer l'emprunt
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

                    loanId = (int)await command.ExecuteScalarAsync();
                }

                // Mettre à jour le statut de l'exemplaire
                var updateCopyQuery = "UPDATE BookCopy SET Status = 'ON_LOAN' WHERE Id = @CopyId";
                using (var command = new SqlCommand(updateCopyQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@CopyId", availableCopy.Id);
                    await command.ExecuteNonQueryAsync();
                }

                // Mettre à jour le compteur de copies disponibles
                var updateBookQuery = @"
                    UPDATE Book 
                    SET AvailableCopiesCount = AvailableCopiesCount - 1 
                    WHERE Id = @BookId";
                using (var command = new SqlCommand(updateBookQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@BookId", request.BookId);
                    await command.ExecuteNonQueryAsync();
                }

                // Mettre à jour les statistiques
                await UpdateBookStatisticsAsync(connection, transaction, request.BookId);

                transaction.Commit();

                var loan = new Loan
                {
                    Id = loanId,
                    BookCopyId = availableCopy.Id,
                    BorrowerId = request.UserId,
                    LoanDate = loanDate,
                    DueDate = dueDate,
                    Status = "ONGOING",
                    BookId = request.BookId,
                    BookTitleSnapshot = bookInfo.Title
                };

                return Ok(new ApiResponse<Loan>
                {
                    Success = true,
                    Data = loan,
                    Message = "Livre emprunté avec succès"
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<Loan>
            {
                Success = false,
                Message = "Erreur lors de l'emprunt du livre",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<Loan>>>> GetUserLoans(Guid userId, [FromQuery] bool activeOnly = true)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var statusFilter = activeOnly ? "WHERE Status = 'ONGOING'" : "";
            var query = $@"
                SELECT Id, BookCopyId, BorrowerId, LoanDate, DueDate, ReturnDate, 
                       Status, RenewalCount, BookId, BookTitleSnapshot
                FROM Loan
                WHERE BorrowerId = @UserId
                {statusFilter}
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

            return Ok(new ApiResponse<List<Loan>>
            {
                Success = true,
                Data = loans,
                Message = $"{loans.Count} emprunt(s) trouvé(s)"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<List<Loan>>
            {
                Success = false,
                Message = "Erreur lors de la récupération des emprunts",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    // Méthodes privées
    private async Task<bool> UserExistsAsync(SqlConnection connection, Guid userId)
    {
        var query = "SELECT COUNT(*) FROM LibraryUser WHERE Id = @UserId AND IsActive = 1";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<int> GetUserActiveLoansCountAsync(SqlConnection connection, Guid userId)
    {
        var query = "SELECT COUNT(*) FROM Loan WHERE BorrowerId = @UserId AND Status = 'ONGOING'";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return (int)await command.ExecuteScalarAsync();
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

    private async Task<bool> HasActiveLoanForBookAsync(SqlConnection connection, Guid userId, int bookId)
    {
        var query = @"
            SELECT COUNT(*) 
            FROM Loan 
            WHERE BorrowerId = @UserId 
            AND BookId = @BookId 
            AND Status = 'ONGOING'";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@BookId", bookId);
        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<Book?> GetBookInfoAsync(SqlConnection connection, int bookId)
    {
        var query = "SELECT Id, Title FROM Book WHERE Id = @BookId";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new Book
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1)
            };
        }

        return null;
    }

    private async Task<LibraryUser?> GetUserInfoAsync(SqlConnection connection, Guid userId)
    {
        var query = "SELECT Id, FirstName, LastName, Email FROM LibraryUser WHERE Id = @UserId";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new LibraryUser
            {
                Id = reader.GetGuid(0),
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
    public Guid UserId { get; set; }
}