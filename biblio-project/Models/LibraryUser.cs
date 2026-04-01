namespace biblio_project.Models;

public class LibraryUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegistrationDate { get; set; }
    
    public string FullName => $"{FirstName} {LastName}";
}