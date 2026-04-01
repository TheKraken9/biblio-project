namespace biblio_project.Models;

public class BookStatistics
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int TotalLoansCount { get; set; }
    public int CurrentActiveLoansCount { get; set; }
    public int TotalReservationsCount { get; set; }
    public int CurrentActiveReservationsCount { get; set; }
    public DateTime? LastLoanDate { get; set; }
    public DateTime? LastReservationDate { get; set; }
    public DateTime UpdatedAt { get; set; }
}
