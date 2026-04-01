using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Text;

namespace biblio_project.Controllers;

public class CatalogController : Controller
{
    private readonly string _connectionString;

    public CatalogController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
    }

    public async Task<IActionResult> Index(string? search, int? categoryId, int? authorId,
        bool? availableOnly, int? year, int page = 1)
    {
        var viewModel = new CatalogViewModel
        {
            SearchQuery = search,
            CategoryId = categoryId,
            AuthorId = authorId,
            AvailableOnly = availableOnly,
            PublicationYear = year,
            CurrentPage = page
        };

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        viewModel.Categories = await GetCategoriesAsync(connection);
        viewModel.Authors = await GetAuthorsAsync(connection);

        var (books, totalCount) = await GetBooksAsync(connection, viewModel);
        viewModel.Books = books;
        viewModel.TotalBooks = totalCount;

        return View(viewModel);
    }

    // ── Détail d'un livre ─────────────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var book = await GetBookDetailAsync(connection, id);
        if (book == null)
            return NotFound();

        // Passer l'userId authentifié à la vue (fix bug Guid.NewGuid())
        if (User.Identity?.IsAuthenticated ?? false)
        {
            ViewData["AuthenticatedUserId"] = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        return View(book);
    }

    // ── Export PDF du catalogue ───────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ExportPdf(string? search, int? categoryId, bool? availableOnly)
    {
        var viewModel = new CatalogViewModel
        {
            SearchQuery = search,
            CategoryId = categoryId,
            AvailableOnly = availableOnly,
            CurrentPage = 1,
            PageSize = 1000 // Exporter tout
        };

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        viewModel.Categories = await GetCategoriesAsync(connection);
        var (books, total) = await GetBooksAsync(connection, viewModel);
        viewModel.Books = books;
        viewModel.TotalBooks = total;

        // Générer HTML → le contrôleur retourne la vue PDF spéciale
        return View("ExportPdf", viewModel);
    }

    // ── Méthodes privées ──────────────────────────────────────────────────────

    private async Task<List<Category>> GetCategoriesAsync(SqlConnection connection)
    {
        var categories = new List<Category>();
        var query = "SELECT Id, Name, Description FROM Category ORDER BY Name";

        using var command = new SqlCommand(query, connection);
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

    private async Task<List<Author>> GetAuthorsAsync(SqlConnection connection)
    {
        var authors = new List<Author>();
        var query = @"
            SELECT DISTINCT a.Id, a.FirstName, a.LastName, a.BirthYear, a.DeathYear
            FROM Author a
            INNER JOIN BookAuthors ba ON a.Id = ba.AuthorId
            ORDER BY a.LastName, a.FirstName";

        using var command = new SqlCommand(query, connection);
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

    private async Task<(List<Book>, int)> GetBooksAsync(SqlConnection connection, CatalogViewModel model)
    {
        var books = new List<Book>();
        var whereClause = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(model.SearchQuery))
        {
            // Recherche full-text avec CONTAINS si l'index existe, fallback LIKE
            whereClause.Add(@"(b.Title LIKE @Search
                               OR b.AuthorNamesText LIKE @Search
                               OR b.CategoryNamesText LIKE @Search)");
            parameters.Add(new SqlParameter("@Search", $"%{model.SearchQuery}%"));
        }

        if (model.CategoryId.HasValue)
        {
            whereClause.Add("EXISTS (SELECT 1 FROM BookCategories bc WHERE bc.BookId = b.Id AND bc.CategoryId = @CategoryId)");
            parameters.Add(new SqlParameter("@CategoryId", model.CategoryId.Value));
        }

        if (model.AuthorId.HasValue)
        {
            whereClause.Add("EXISTS (SELECT 1 FROM BookAuthors ba WHERE ba.BookId = b.Id AND ba.AuthorId = @AuthorId)");
            parameters.Add(new SqlParameter("@AuthorId", model.AuthorId.Value));
        }

        if (model.AvailableOnly == true)
        {
            whereClause.Add("b.AvailableCopiesCount > 0");
        }

        if (model.PublicationYear.HasValue)
        {
            whereClause.Add("b.PublicationYear = @Year");
            parameters.Add(new SqlParameter("@Year", model.PublicationYear.Value));
        }

        var whereCondition = whereClause.Count > 0 ? "WHERE " + string.Join(" AND ", whereClause) : "";

        // COUNT (une seule fois)
        var countQuery = $"SELECT COUNT(*) FROM Books b {whereCondition}";
        int totalCount;
        using (var countCommand = new SqlCommand(countQuery, connection))
        {
            countCommand.Parameters.AddRange(parameters.Select(p =>
                new SqlParameter(p.ParameterName, p.Value)).ToArray());
            totalCount = (int)await countCommand.ExecuteScalarAsync();
        }

        if (totalCount == 0)
            return (books, 0);

        // Livres paginés
        var offset = (model.CurrentPage - 1) * model.PageSize;
        var query = $@"
            SELECT b.Id, b.Title, b.Subtitle, b.PublicationYear,
                   b.CoverImageUrl, b.AuthorNamesText, b.CategoryNamesText,
                   b.AvailableCopiesCount, b.TotalCopiesCount,
                   p.Name as PublisherName
            FROM Books b
            LEFT JOIN Publisher p ON b.PublisherId = p.Id
            {whereCondition}
            ORDER BY b.Title
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddRange(parameters.Select(p =>
            new SqlParameter(p.ParameterName, p.Value)).ToArray());
        command.Parameters.Add(new SqlParameter("@Offset", offset));
        command.Parameters.Add(new SqlParameter("@PageSize", model.PageSize));

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
                TotalCopiesCount = reader.GetInt32(8),
                PublisherName = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return (books, totalCount);
    }

    private async Task<BookDetail?> GetBookDetailAsync(SqlConnection connection, int bookId)
    {
        BookDetail? book = null;

        var query = @"
            SELECT b.Id, b.Title, b.Subtitle, b.PublicationYear,
                   b.CoverImageUrl, b.AuthorNamesText, b.CategoryNamesText,
                   b.AvailableCopiesCount, b.TotalCopiesCount,
                   p.Name as PublisherName
            FROM Books b
            LEFT JOIN Publisher p ON b.PublisherId = p.Id
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
                    PublicationYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    CoverImageUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    AuthorNamesText = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CategoryNamesText = reader.IsDBNull(6) ? null : reader.GetString(6),
                    AvailableCopiesCount = reader.GetInt32(7),
                    TotalCopiesCount = reader.GetInt32(8),
                    PublisherName = reader.IsDBNull(9) ? null : reader.GetString(9)
                };
            }
        }

        if (book == null) return null;

        book.Authors = await GetBookAuthorsAsync(connection, bookId);
        book.Categories = await GetBookCategoriesAsync(connection, bookId);

        return book;
    }

    private async Task<List<Author>> GetBookAuthorsAsync(SqlConnection connection, int bookId)
    {
        var authors = new List<Author>();
        var query = @"
            SELECT a.Id, a.FirstName, a.LastName, a.BirthYear, a.DeathYear, a.Bio
            FROM Author a
            INNER JOIN BookAuthors ba ON a.Id = ba.AuthorId
            WHERE ba.BookId = @BookId
            ORDER BY a.LastName, a.FirstName";

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
                DeathYear = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Bio = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return authors;
    }

    private async Task<List<Category>> GetBookCategoriesAsync(SqlConnection connection, int bookId)
    {
        var categories = new List<Category>();
        var query = @"
            SELECT c.Id, c.Name, c.Description
            FROM Category c
            INNER JOIN BookCategories bc ON c.Id = bc.CategoryId
            WHERE bc.BookId = @BookId
            ORDER BY c.Name";

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
