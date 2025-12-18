namespace biblio_project.Models;

public class Book
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
    public int? MainCategoryId { get; set; }
    public string? MainCategoryName { get; set; }
    public int? PublisherId { get; set; }
    public string? PublisherName { get; set; }
}