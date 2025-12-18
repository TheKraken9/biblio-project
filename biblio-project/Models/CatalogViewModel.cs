namespace biblio_project.Models;

public class CatalogViewModel
{
    public List<Book> Books { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Author> Authors { get; set; } = new();
    
    // Filtres
    public string? SearchQuery { get; set; }
    public int? CategoryId { get; set; }
    public int? AuthorId { get; set; }
    public bool? AvailableOnly { get; set; }
    public int? PublicationYear { get; set; }
    
    // Pagination
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int TotalBooks { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalBooks / PageSize);
}