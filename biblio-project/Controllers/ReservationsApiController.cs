using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

[ApiController]
[Route("api/reservations")]
public class ReservationsApiController : ControllerBase
{
    private readonly string _connectionString;
    private readonly int _maxReservations;

    public ReservationsApiController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _maxReservations = 3;
    }

    [HttpPost("reserve")]
    public async Task<ActionResult<ApiResponse<Reservation>>> ReserveBook([FromBody] ReserveBookRequest request)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Vérifier que l'utilisateur existe
            if (!await UserExistsAsync(connection, request.UserId))
            {
                return BadRequest(new ApiResponse<Reservation>
                {
                    Success = false,
                    Message = "Utilisateur non trouvé"
                });
            }

            // Vérifier le nombre de réservations actives
            var activeReservationsCount = await GetUserActiveReservationsCountAsync(connection, request.UserId);
            if (activeReservationsCount >= _maxReservations)
            {
                return BadRequest(new ApiResponse<Reservation>
                {
                    Success = false,
                    Message = $"Vous avez atteint la limite de {_maxReservations} réservations actives"
                });
            }

            // Vérifier si l'utilisateur n'a pas déjà réservé ce livre
            if (await HasActiveReservationForBookAsync(connection, request.UserId, request.BookId))
            {
                return BadRequest(new ApiResponse<Reservation>
                {
                    Success = false,
                    Message = "Vous avez déjà une réservation active pour ce livre"
                });
            }

            // Vérifier si le livre existe
            var bookInfo = await GetBookInfoAsync(connection, request.BookId);
            if (bookInfo == null)
            {
                return NotFound(new ApiResponse<Reservation>
                {
                    Success = false,
                    Message = "Livre non trouvé"
                });
            }

            // Récupérer les informations de l'utilisateur
            var userInfo = await GetUserInfoAsync(connection, request.UserId);
            if (userInfo == null)
            {
                return BadRequest(new ApiResponse<Reservation>
                {
                    Success = false,
                    Message = "Utilisateur invalide"
                });
            }

            // Calculer la position dans la file d'attente
            var position = await GetNextQueuePositionAsync(connection, request.BookId);

            using var transaction = connection.BeginTransaction();
            try
            {
                var insertQuery = @"
                    INSERT INTO Reservations (BookId, RequesterId, Status, PositionInQueue, RequestedAt,
                                             BookTitleSnapshot, RequesterNameSnapshot, CreatedAt, UpdatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@BookId, @RequesterId, 'Pending', @Position, GETDATE(),
                            @BookTitle, @RequesterName, GETDATE(), GETDATE())";

                int reservationId;
                using (var command = new SqlCommand(insertQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@BookId", request.BookId);
                    command.Parameters.AddWithValue("@RequesterId", request.UserId);
                    command.Parameters.AddWithValue("@Position", position);
                    command.Parameters.AddWithValue("@BookTitle", bookInfo.Title);
                    command.Parameters.AddWithValue("@RequesterName", userInfo.FullName);

                    reservationId = (int)await command.ExecuteScalarAsync();
                }

                transaction.Commit();

                var reservation = new Reservation
                {
                    Id = reservationId,
                    BookId = request.BookId,
                    RequesterId = request.UserId,
                    Status = "Pending",
                    PositionInQueue = position,
                    RequestedAt = DateTime.Now,
                    BookTitleSnapshot = bookInfo.Title,
                    RequesterNameSnapshot = userInfo.FullName
                };

                return Ok(new ApiResponse<Reservation>
                {
                    Success = true,
                    Data = reservation,
                    Message = $"Réservation créée avec succès. Position dans la file : {position}"
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
            return StatusCode(500, new ApiResponse<Reservation>
            {
                Success = false,
                Message = "Erreur lors de la création de la réservation",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<Reservation>>>> GetUserReservations(int userId, [FromQuery] bool activeOnly = true)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var statusFilter = activeOnly ? "AND Status IN ('Pending', 'ReadyForPickup')" : "";
            var query = $@"
                SELECT Id, BookId, RequesterId, Status, PositionInQueue,
                       RequestedAt, ExpireAt, BookTitleSnapshot
                FROM Reservations
                WHERE RequesterId = @UserId
                {statusFilter}
                ORDER BY RequestedAt DESC";

            var reservations = new List<Reservation>();
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

            return Ok(new ApiResponse<List<Reservation>>
            {
                Success = true,
                Data = reservations,
                Message = $"{reservations.Count} réservation(s) trouvée(s)"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<List<Reservation>>
            {
                Success = false,
                Message = "Erreur lors de la récupération des réservations",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    [HttpDelete("{reservationId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelReservation(int reservationId, [FromQuery] int userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Vérifier que la réservation appartient à l'utilisateur
            var checkQuery = @"
                SELECT BookId, Status
                FROM Reservations
                WHERE Id = @ReservationId AND RequesterId = @UserId";

            int bookId;
            string status;
            using (var command = new SqlCommand(checkQuery, connection))
            {
                command.Parameters.AddWithValue("@ReservationId", reservationId);
                command.Parameters.AddWithValue("@UserId", userId);
                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return NotFound(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Réservation non trouvée"
                    });
                }

                bookId = reader.GetInt32(0);
                status = reader.GetString(1);
            }

            if (status == "Fulfilled" || status == "Cancelled")
            {
                return BadRequest(new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Cette réservation ne peut pas être annulée"
                });
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                // Annuler la réservation
                var updateQuery = @"
                    UPDATE Reservations
                    SET Status = 'Cancelled', UpdatedAt = GETDATE()
                    WHERE Id = @ReservationId";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@ReservationId", reservationId);
                    await command.ExecuteNonQueryAsync();
                }

                // Réorganiser la file d'attente
                await ReorganizeQueueAsync(connection, transaction, bookId);

                transaction.Commit();

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Data = true,
                    Message = "Réservation annulée avec succès"
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
            return StatusCode(500, new ApiResponse<bool>
            {
                Success = false,
                Message = "Erreur lors de l'annulation de la réservation",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    // Méthodes privées
    private async Task<bool> UserExistsAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT COUNT(*) FROM library_user WHERE Id = @UserId AND IsActive = 1";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<int> GetUserActiveReservationsCountAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT COUNT(*) FROM Reservations WHERE RequesterId = @UserId AND Status IN ('Pending', 'ReadyForPickup')";
        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return (int)await command.ExecuteScalarAsync();
    }

    private async Task<bool> HasActiveReservationForBookAsync(SqlConnection connection, int userId, int bookId)
    {
        var query = @"
            SELECT COUNT(*)
            FROM Reservations
            WHERE RequesterId = @UserId
            AND BookId = @BookId
            AND Status IN ('Pending', 'ReadyForPickup')";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@BookId", bookId);
        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<Book?> GetBookInfoAsync(SqlConnection connection, int bookId)
    {
        var query = "SELECT Id, Title FROM Books WHERE Id = @BookId";
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

    private async Task<LibraryUser?> GetUserInfoAsync(SqlConnection connection, int userId)
    {
        var query = "SELECT Id, FirstName, LastName, Email FROM library_user WHERE Id = @UserId";
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

    private async Task<int> GetNextQueuePositionAsync(SqlConnection connection, int bookId)
    {
        var query = @"
            SELECT ISNULL(MAX(PositionInQueue), 0) + 1
            FROM Reservations
            WHERE BookId = @BookId AND Status = 'Pending'";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        return (int)await command.ExecuteScalarAsync();
    }

    private async Task ReorganizeQueueAsync(SqlConnection connection, SqlTransaction transaction, int bookId)
    {
        var query = @"
            WITH OrderedReservations AS (
                SELECT Id, ROW_NUMBER() OVER (ORDER BY RequestedAt) as NewPosition
                FROM Reservations
                WHERE BookId = @BookId AND Status = 'Pending'
            )
            UPDATE r
            SET r.PositionInQueue = o.NewPosition
            FROM Reservations r
            INNER JOIN OrderedReservations o ON r.Id = o.Id";

        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        await command.ExecuteNonQueryAsync();
    }
}

public class ReserveBookRequest
{
    public int BookId { get; set; }
    public int UserId { get; set; }
}
