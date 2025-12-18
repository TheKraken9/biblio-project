namespace biblio_project.Models;

public class UserDashboardViewModel
{
    public LibraryUser User { get; set; } = null!;
    public List<Loan> CurrentLoans { get; set; } = new();
    public List<Reservation> ActiveReservations { get; set; } = new();
    public int MaxConcurrentLoans { get; set; }
    public int MaxReservations { get; set; }
    
    public bool CanBorrowMore => CurrentLoans.Count < MaxConcurrentLoans;
    public bool CanReserveMore => ActiveReservations.Count < MaxReservations;
}