using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using Microsoft.Data.SqlClient;

namespace biblio_project.Controllers;

[ApiController]
[Route("api/books")]
public class BooksApiController : ControllerBase
{
    private readonly string _connectionString;

    public BooksApiController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Book>>>> GetBooks(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] bool? availableOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var whereClause = new List<string>();
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereClause.Add("(b.Title LIKE @Search OR b.AuthorNamesText LIKE @Search OR b.Keyword LIKE @Search)");
                parameters.Add(new SqlParameter("@Search", $"%{search}%"));
            }

            if (categoryId.HasValue)
            {
                whereClause.Add("EXISTS (SELECT 1 FROM BookCategories bc WHERE bc.BookId = b.Id AND bc.CategoryId = @CategoryId)");
                parameters.Add(new SqlParameter("@CategoryId", categoryId.Value));
            }

            if (availableOnly == true)
            {
                whereClause.Add("b.AvailableCopiesCount > 0");
            }

            var whereCondition = whereClause.Count > 0 ? "WHERE " + string.Join(" AND ", whereClause) : "";
            var offset = (page - 1) * pageSize;

            var query = $@"
                SELECT b.Id, b.Title, b.Subtitle, b.PublicationYear,
                       b.CoverImageUrl, b.AuthorNamesText, b.CategoryNamesText,
                       b.AvailableCopiesCount, b.TotalCopiesCount
                FROM Books b
                {whereCondition}
                ORDER BY b.Title
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var books = new List<Book>();
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(parameters.ToArray());
            command.Parameters.Add(new SqlParameter("@Offset", offset));
            command.Parameters.Add(new SqlParameter("@PageSize", pageSize));

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                books.Add(new Book
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Subtitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PublicationYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    CoverImageUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    AuthorNamesText = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CategoryNamesText = reader.IsDBNull(6) ? null : reader.GetString(6),
                    AvailableCopiesCount = reader.GetInt32(7),
                    TotalCopiesCount = reader.GetInt32(8)
                });
            }

            return Ok(new ApiResponse<List<Book>>
            {
                Success = true,
                Data = books,
                Message = $"{books.Count} livres trouvés"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<List<Book>>
            {
                Success = false,
                Message = "Erreur lors de la récupération des livres",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BookDetail>>> GetBookById(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var book = await GetBookDetailAsync(connection, id);

            if (book == null)
            {
                return NotFound(new ApiResponse<BookDetail>
                {
                    Success = false,
                    Message = "Livre non trouvé"
                });
            }

            return Ok(new ApiResponse<BookDetail>
            {
                Success = true,
                Data = book
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<BookDetail>
            {
                Success = false,
                Message = "Erreur lors de la récupération du livre",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    private async Task<BookDetail?> GetBookDetailAsync(SqlConnection connection, int bookId)
    {
        BookDetail? book = null;

        var query = @"
            SELECT b.Id, b.Title, b.Subtitle, b.Keyword, b.PublicationYear,
                   b.CoverImageUrl, b.AuthorNamesText, b.CategoryNamesText,
                   b.AvailableCopiesCount, b.TotalCopiesCount,
                   p.Name as PublisherName
            FROM Books b
            LEFT JOIN publisher p ON b.PublisherId = p.Id
            WHERE b.Id = @BookId";

        using (var command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@BookId", bookId);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                book = new BookDetail
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Subtitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Summary = reader.IsDBNull(3) ? null : reader.GetString(3), // Keyword comme résumé
                    PublicationYear = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    CoverImageUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                    AuthorNamesText = reader.IsDBNull(6) ? null : reader.GetString(6),
                    CategoryNamesText = reader.IsDBNull(7) ? null : reader.GetString(7),
                    AvailableCopiesCount = reader.GetInt32(8),
                    TotalCopiesCount = reader.GetInt32(9),
                    PublisherName = reader.IsDBNull(10) ? null : reader.GetString(10)
                };
            }
        }

        if (book != null)
        {
            book.Authors = await GetBookAuthorsAsync(connection, bookId);
            book.Categories = await GetBookCategoriesAsync(connection, bookId);
        }

        return book;
    }

    private async Task<List<Author>> GetBookAuthorsAsync(SqlConnection connection, int bookId)
    {
        var authors = new List<Author>();
        var query = @"
            SELECT a.Id, a.FirstName, a.LastName, a.BirthYear, a.DeathYear
            FROM author a
            INNER JOIN BookAuthors ba ON a.Id = ba.AuthorId
            WHERE ba.BookId = @BookId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            authors.Add(new Author
            {
                Id = reader.GetInt32(0),
                FirstName = reader.IsDBNull(1) ? null : reader.GetString(1),
                LastName = reader.GetString(2),
                BirthYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                DeathYear = reader.IsDBNull(4) ? null : reader.GetInt32(4)
            });
        }

        return authors;
    }

    private async Task<List<Category>> GetBookCategoriesAsync(SqlConnection connection, int bookId)
    {
        var categories = new List<Category>();
        var query = @"
            SELECT c.Id, c.Name, c.Description
            FROM category c
            INNER JOIN BookCategories bc ON c.Id = bc.CategoryId
            WHERE bc.BookId = @BookId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            categories.Add(new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return categories;
    }
}
