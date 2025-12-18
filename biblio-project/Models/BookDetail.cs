namespace biblio_project.Models;

public class BookDetail
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Summary { get; set; }
    public int? PublicationYear { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? AuthorNamesText { get; set; }
    public string? CategoryNamesText { get; set; }
    public int AvailableCopiesCount { get; set; }
    public int TotalCopiesCount { get; set; }
    public string? PublisherName { get; set; }
    public bool IsAvailable => AvailableCopiesCount > 0;
    public List<Author> Authors { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
}