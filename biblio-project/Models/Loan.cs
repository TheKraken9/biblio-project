namespace biblio_project.Models;

public class Loan
{
    public int Id { get; set; }
    public int BookCopyId { get; set; }
    public int BorrowerId { get; set; }
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string Status { get; set; } = "OnLoan";
    public int RenewalCount { get; set; }
    public int BookId { get; set; }
    
    public string? BookTitleSnapshot { get; set; }
    public string? BorrowerNameSnapshot { get; set; }
    
    public bool IsOverdue => ReturnDate == null && DateTime.Now > DueDate;
    public int DaysUntilDue => (DueDate - DateTime.Now).Days;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}