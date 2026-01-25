using Microsoft.AspNetCore.Mvc;
using biblio_project.Models;
using Microsoft.Data.SqlClient;

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

        // Récupérer les catégories pour les filtres
        viewModel.Categories = await GetCategoriesAsync(connection);

        // Récupérer les auteurs pour les filtres
        viewModel.Authors = await GetAuthorsAsync(connection);

        // Récupérer les livres avec filtres
        var (books, totalCount) = await GetBooksAsync(connection, viewModel);
        viewModel.Books = books;
        viewModel.TotalBooks = totalCount;

        return View(viewModel);
    }

    private async Task<List<Category>> GetCategoriesAsync(SqlConnection connection)
    {
        var categories = new List<Category>();
        var query = "SELECT Id, Name, Description FROM category ORDER BY Name";

        using var command = new SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            categories.Add(new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Slug = reader.GetString(1).ToLower().Replace(" ", "-"), // Générer slug à partir du nom
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
            FROM author a
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

        // Construction de la requête avec filtres
        var whereClause = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(model.SearchQuery))
        {
            whereClause.Add("(b.Title LIKE @Search OR b.AuthorNamesText LIKE @Search OR b.Keyword LIKE @Search OR b.NormalizedTitle LIKE @Search)");
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

        // Compter le total d'abord
        var countQuery = $"SELECT COUNT(*) FROM Books b {whereCondition}";
        int totalCount;

        using (var countCommand = new SqlCommand(countQuery, connection))
        {
            // Recréer les paramètres pour cette commande
            foreach (var param in parameters)
            {
                countCommand.Parameters.AddWithValue(param.ParameterName, param.Value);
            }
            totalCount = (int)await countCommand.ExecuteScalarAsync();

            if (totalCount == 0)
                return (books, 0);
        }

        // Récupérer les livres paginés
        var offset = (model.CurrentPage - 1) * model.PageSize;
        var query = $@"
            SELECT b.Id, b.Title, b.Subtitle, b.Keyword, b.PublicationYear,
                   b.CoverImageUrl, b.AuthorNamesText, b.CategoryNamesText,
                   b.AvailableCopiesCount, b.TotalCopiesCount,
                   (SELECT TOP 1 c.Name FROM category c
                    INNER JOIN BookCategories bc ON c.Id = bc.CategoryId
                    WHERE bc.BookId = b.Id) as MainCategoryName,
                   p.Name as PublisherName
            FROM Books b
            LEFT JOIN publisher p ON b.PublisherId = p.Id
            {whereCondition}
            ORDER BY b.Title
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        using (var command = new SqlCommand(query, connection))
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.ParameterName, param.Value);
            }
            command.Parameters.AddWithValue("@Offset", offset);
            command.Parameters.AddWithValue("@PageSize", model.PageSize);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    books.Add(new Book
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
                        MainCategoryName = reader.IsDBNull(10) ? null : reader.GetString(10),
                        PublisherName = reader.IsDBNull(11) ? null : reader.GetString(11)
                    });
                }
            }
        }

        return (books, totalCount);
    }

    public async Task<IActionResult> Details(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var book = await GetBookDetailAsync(connection, id);

        if (book == null)
            return NotFound();

        return View(book);
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

        if (book == null)
            return null;

        // Récupérer les auteurs
        book.Authors = await GetBookAuthorsAsync(connection, bookId);

        // Récupérer les catégories
        book.Categories = await GetBookCategoriesAsync(connection, bookId);

        return book;
    }

    private async Task<List<Author>> GetBookAuthorsAsync(SqlConnection connection, int bookId)
    {
        var authors = new List<Author>();
        var query = @"
            SELECT a.Id, a.FirstName, a.LastName, a.BirthYear, a.DeathYear, a.Bio
            FROM author a
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
            FROM category c
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
                Slug = reader.GetString(1).ToLower().Replace(" ", "-"),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return categories;
    }
}
