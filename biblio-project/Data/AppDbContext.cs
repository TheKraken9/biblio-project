using biblio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace BiblioProject.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Exemple
    public DbSet<Book> Books => Set<Book>();
}