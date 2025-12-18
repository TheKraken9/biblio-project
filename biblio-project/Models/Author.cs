namespace biblio_project.Models;

public class Author
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public int? DeathYear { get; set; }
    public string? Bio { get; set; }
    
    public string FullName => string.IsNullOrEmpty(FirstName) 
        ? LastName 
        : $"{FirstName} {LastName}";
}