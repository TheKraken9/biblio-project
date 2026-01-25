namespace biblio_project.Models;

public class Reservation
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int RequesterId { get; set; }
    public int? AssignedCopyId { get; set; }
    public string Status { get; set; } = "PENDING";
    public int PositionInQueue { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? BookTitleSnapshot { get; set; }
    public string? RequesterNameSnapshot { get; set; }
}