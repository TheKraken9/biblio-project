namespace biblio_project.Models;

public class BookCopy
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public DateTime AcquisitionDate { get; set; }
    public string Status { get; set; } = "AVAILABLE";
    public bool IsReferenceOnly { get; set; }
}