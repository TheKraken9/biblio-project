using Microsoft.Data.SqlClient;

namespace biblio_project.Services;

public interface IDataSeeder
{
    Task SeedAsync();
}

public class DataSeeder : IDataSeeder
{
    private readonly string _connectionString;
    private readonly IPasswordHasher _passwordHasher;

    public DataSeeder(IConfiguration configuration, IPasswordHasher passwordHasher)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _passwordHasher = passwordHasher;
    }

    public async Task SeedAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Vérifier si des données existent déjà
        if (await HasDataAsync(connection))
        {
            return; // Données déjà présentes, ne pas dupliquer
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Insérer les rôles
            await SeedRolesAsync(connection, transaction);

            // 2. Insérer les catégories
            await SeedCategoriesAsync(connection, transaction);

            // 3. Insérer les auteurs
            await SeedAuthorsAsync(connection, transaction);

            // 4. Insérer les éditeurs
            await SeedPublishersAsync(connection, transaction);

            // 5. Insérer les livres
            await SeedBooksAsync(connection, transaction);

            // 6. Insérer les associations livre-auteur
            await SeedBookAuthorsAsync(connection, transaction);

            // 7. Insérer les associations livre-catégorie
            await SeedBookCategoriesAsync(connection, transaction);

            // 8. Insérer les copies de livres
            await SeedBookCopiesAsync(connection, transaction);

            // 9. Insérer un utilisateur de test
            await SeedTestUserAsync(connection, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<bool> HasDataAsync(SqlConnection connection)
    {
        var query = "SELECT COUNT(*) FROM Books";
        using var command = new SqlCommand(query, connection);
        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task SeedRolesAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var roles = new[]
        {
            ("ADMIN", "Administrateur du système"),
            ("LIBRARIAN", "Bibliothécaire"),
            ("MEMBER", "Membre de la bibliothèque")
        };

        var checkQuery = "SELECT COUNT(*) FROM role WHERE Name = @Name";
        var insertQuery = @"
            INSERT INTO role (Name, Description)
            VALUES (@Name, @Description)";

        foreach (var (name, description) in roles)
        {
            using var checkCmd = new SqlCommand(checkQuery, connection, transaction);
            checkCmd.Parameters.AddWithValue("@Name", name);
            var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;

            if (!exists)
            {
                using var insertCmd = new SqlCommand(insertQuery, connection, transaction);
                insertCmd.Parameters.AddWithValue("@Name", name);
                insertCmd.Parameters.AddWithValue("@Description", description);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task SeedCategoriesAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var categories = new[]
        {
            ("Roman", "Romans et fiction littéraire"),
            ("Science-Fiction", "Science-fiction et fantastique"),
            ("Policier", "Romans policiers et thrillers"),
            ("Histoire", "Livres d'histoire et biographies"),
            ("Sciences", "Sciences et techniques"),
            ("Philosophie", "Philosophie et pensée"),
            ("Jeunesse", "Littérature jeunesse"),
            ("Poésie", "Poésie et théâtre"),
            ("Art", "Art et beaux livres"),
            ("Informatique", "Informatique et programmation")
        };

        var insertQuery = @"
            INSERT INTO category (Name, Description, CreatedAt, UpdatedAt)
            VALUES (@Name, @Description, GETDATE(), GETDATE())";

        foreach (var (name, description) in categories)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", description);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedAuthorsAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var authors = new[]
        {
            ("Victor", "Hugo", 1802, 1885, "Écrivain, dramaturge, poète, homme politique et académicien français, considéré comme l'un des plus grands écrivains de la langue française."),
            ("Albert", "Camus", 1913, 1960, "Écrivain, philosophe, romancier, dramaturge, essayiste et nouvelliste français. Prix Nobel de littérature en 1957."),
            ("Émile", "Zola", 1840, 1902, "Écrivain et journaliste français, considéré comme le chef de file du naturalisme."),
            ("Marcel", "Proust", 1871, 1922, "Écrivain français, dont l'œuvre principale est la suite romanesque intitulée À la recherche du temps perdu."),
            ("Alexandre", "Dumas", 1802, 1870, "Écrivain français, auteur de romans populaires comme Les Trois Mousquetaires."),
            ("Jules", "Verne", 1828, 1905, "Écrivain français dont l'œuvre est pour la plus grande partie constituée de romans d'aventures et de science-fiction."),
            ("Gustave", "Flaubert", 1821, 1880, "Écrivain français, auteur de Madame Bovary."),
            ("Honoré", "Balzac", 1799, 1850, "Écrivain français, auteur de La Comédie humaine."),
            ("Agatha", "Christie", 1890, 1976, "Écrivain britannique, auteure de romans policiers mondialement connus."),
            ("Isaac", "Asimov", 1920, 1992, "Écrivain américano-russe, auteur de science-fiction et de vulgarisation scientifique.")
        };

        var insertQuery = @"
            INSERT INTO author (FirstName, LastName, BirthYear, DeathYear, Bio, CreatedAt, UpdatedAt)
            VALUES (@FirstName, @LastName, @BirthYear, @DeathYear, @Bio, GETDATE(), GETDATE())";

        foreach (var (firstName, lastName, birthYear, deathYear, bio) in authors)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", lastName);
            cmd.Parameters.AddWithValue("@BirthYear", birthYear);
            cmd.Parameters.AddWithValue("@DeathYear", deathYear);
            cmd.Parameters.AddWithValue("@Bio", bio);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedPublishersAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var publishers = new[]
        {
            ("Gallimard", "France", "Paris"),
            ("Flammarion", "France", "Paris"),
            ("Hachette", "France", "Paris"),
            ("Le Livre de Poche", "France", "Paris"),
            ("Folio", "France", "Paris")
        };

        var insertQuery = @"
            INSERT INTO publisher (Name, Country, City, CreatedAt, UpdatedAt)
            VALUES (@Name, @Country, @City, GETDATE(), GETDATE())";

        foreach (var (name, country, city) in publishers)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Country", country);
            cmd.Parameters.AddWithValue("@City", city);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedBooksAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var books = new[]
        {
            ("Les Misérables", "les miserables", "", 1862, 1, "/uploads/covers/miserables.jpg", "Victor Hugo", "Roman", "misérables hugo roman classique", 3, 3),
            ("Notre-Dame de Paris", "notre dame de paris", "", 1831, 1, "/uploads/covers/notredame.jpg", "Victor Hugo", "Roman", "notre dame paris hugo roman", 2, 2),
            ("L'Étranger", "l etranger", "Roman", 1942, 1, "/uploads/covers/etranger.jpg", "Albert Camus", "Roman, Philosophie", "étranger camus absurde philosophie", 2, 2),
            ("La Peste", "la peste", "Chronique", 1947, 1, "/uploads/covers/peste.jpg", "Albert Camus", "Roman", "peste camus roman algérie", 1, 1),
            ("Germinal", "germinal", "", 1885, 2, "/uploads/covers/germinal.jpg", "Émile Zola", "Roman", "germinal zola naturalisme mine", 2, 2),
            ("Du côté de chez Swann", "du cote de chez swann", "À la recherche du temps perdu - Tome 1", 1913, 1, "/uploads/covers/swann.jpg", "Marcel Proust", "Roman", "proust recherche temps perdu swann", 1, 1),
            ("Les Trois Mousquetaires", "les trois mousquetaires", "", 1844, 3, "/uploads/covers/mousquetaires.jpg", "Alexandre Dumas", "Roman", "mousquetaires dumas aventure", 3, 3),
            ("Le Comte de Monte-Cristo", "le comte de monte cristo", "", 1844, 3, "/uploads/covers/montecristo.jpg", "Alexandre Dumas", "Roman", "monte cristo dumas aventure vengeance", 2, 2),
            ("Vingt Mille Lieues sous les mers", "vingt mille lieues sous les mers", "", 1870, 4, "/uploads/covers/nautilus.jpg", "Jules Verne", "Science-Fiction", "verne nautilus nemo science fiction", 2, 2),
            ("Le Tour du monde en quatre-vingts jours", "le tour du monde en quatre vingts jours", "", 1872, 4, "/uploads/covers/80jours.jpg", "Jules Verne", "Roman", "verne tour monde fogg aventure", 2, 2),
            ("Madame Bovary", "madame bovary", "Mœurs de province", 1857, 2, "/uploads/covers/bovary.jpg", "Gustave Flaubert", "Roman", "bovary flaubert réalisme province", 1, 1),
            ("Le Père Goriot", "le pere goriot", "", 1835, 1, "/uploads/covers/goriot.jpg", "Honoré Balzac", "Roman", "goriot balzac comédie humaine paris", 2, 2),
            ("Le Crime de l'Orient-Express", "le crime de l orient express", "", 1934, 5, "/uploads/covers/orient.jpg", "Agatha Christie", "Policier", "christie poirot orient express policier", 2, 2),
            ("Dix Petits Nègres", "dix petits negres", "", 1939, 5, "/uploads/covers/dixpetits.jpg", "Agatha Christie", "Policier", "christie policier mystère île", 2, 2),
            ("Fondation", "fondation", "Cycle de Fondation - Tome 1", 1951, 1, "/uploads/covers/fondation.jpg", "Isaac Asimov", "Science-Fiction", "asimov fondation science fiction espace", 2, 2)
        };

        var insertQuery = @"
            INSERT INTO Books (Title, NormalizedTitle, Subtitle, PublicationYear, PublisherId, CoverImageUrl,
                              AuthorNamesText, CategoryNamesText, Keyword, TotalCopiesCount, AvailableCopiesCount,
                              CreatedAt, UpdatedAt)
            VALUES (@Title, @NormalizedTitle, @Subtitle, @PublicationYear, @PublisherId, @CoverImageUrl,
                    @AuthorNamesText, @CategoryNamesText, @Keyword, @TotalCopiesCount, @AvailableCopiesCount,
                    GETDATE(), GETDATE())";

        foreach (var (title, normalizedTitle, subtitle, year, publisherId, coverUrl, authors, categories, keywords, total, available) in books)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@Title", title);
            cmd.Parameters.AddWithValue("@NormalizedTitle", normalizedTitle);
            cmd.Parameters.AddWithValue("@Subtitle", subtitle);
            cmd.Parameters.AddWithValue("@PublicationYear", year);
            cmd.Parameters.AddWithValue("@PublisherId", publisherId);
            cmd.Parameters.AddWithValue("@CoverImageUrl", coverUrl);
            cmd.Parameters.AddWithValue("@AuthorNamesText", authors);
            cmd.Parameters.AddWithValue("@CategoryNamesText", categories);
            cmd.Parameters.AddWithValue("@Keyword", keywords);
            cmd.Parameters.AddWithValue("@TotalCopiesCount", total);
            cmd.Parameters.AddWithValue("@AvailableCopiesCount", available);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedBookAuthorsAsync(SqlConnection connection, SqlTransaction transaction)
    {
        // Associations livre-auteur (BookId, AuthorId) basées sur l'ordre d'insertion
        var bookAuthors = new[]
        {
            (1, 1),   // Les Misérables - Victor Hugo
            (2, 1),   // Notre-Dame de Paris - Victor Hugo
            (3, 2),   // L'Étranger - Albert Camus
            (4, 2),   // La Peste - Albert Camus
            (5, 3),   // Germinal - Émile Zola
            (6, 4),   // Du côté de chez Swann - Marcel Proust
            (7, 5),   // Les Trois Mousquetaires - Alexandre Dumas
            (8, 5),   // Le Comte de Monte-Cristo - Alexandre Dumas
            (9, 6),   // Vingt Mille Lieues sous les mers - Jules Verne
            (10, 6),  // Le Tour du monde - Jules Verne
            (11, 7),  // Madame Bovary - Gustave Flaubert
            (12, 8),  // Le Père Goriot - Honoré Balzac
            (13, 9),  // Le Crime de l'Orient-Express - Agatha Christie
            (14, 9),  // Dix Petits Nègres - Agatha Christie
            (15, 10)  // Fondation - Isaac Asimov
        };

        var insertQuery = @"
            INSERT INTO BookAuthors (BookId, AuthorId)
            VALUES (@BookId, @AuthorId)";

        foreach (var (bookId, authorId) in bookAuthors)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@BookId", bookId);
            cmd.Parameters.AddWithValue("@AuthorId", authorId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedBookCategoriesAsync(SqlConnection connection, SqlTransaction transaction)
    {
        // Associations livre-catégorie (BookId, CategoryId) basées sur l'ordre d'insertion
        // Catégories: 1=Roman, 2=Science-Fiction, 3=Policier, 4=Histoire, 5=Sciences, 6=Philosophie, 7=Jeunesse, 8=Poésie, 9=Art, 10=Informatique
        var bookCategories = new[]
        {
            (1, 1),   // Les Misérables - Roman
            (2, 1),   // Notre-Dame de Paris - Roman
            (3, 1),   // L'Étranger - Roman
            (3, 6),   // L'Étranger - Philosophie
            (4, 1),   // La Peste - Roman
            (5, 1),   // Germinal - Roman
            (6, 1),   // Du côté de chez Swann - Roman
            (7, 1),   // Les Trois Mousquetaires - Roman
            (8, 1),   // Le Comte de Monte-Cristo - Roman
            (9, 2),   // Vingt Mille Lieues sous les mers - Science-Fiction
            (10, 1),  // Le Tour du monde - Roman
            (11, 1),  // Madame Bovary - Roman
            (12, 1),  // Le Père Goriot - Roman
            (13, 3),  // Le Crime de l'Orient-Express - Policier
            (14, 3),  // Dix Petits Nègres - Policier
            (15, 2)   // Fondation - Science-Fiction
        };

        var insertQuery = @"
            INSERT INTO BookCategories (BookId, CategoryId)
            VALUES (@BookId, @CategoryId)";

        foreach (var (bookId, categoryId) in bookCategories)
        {
            using var cmd = new SqlCommand(insertQuery, connection, transaction);
            cmd.Parameters.AddWithValue("@BookId", bookId);
            cmd.Parameters.AddWithValue("@CategoryId", categoryId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedBookCopiesAsync(SqlConnection connection, SqlTransaction transaction)
    {
        // Récupérer les livres pour créer les copies
        var getBooksQuery = "SELECT Id, Title, CategoryNamesText, AuthorNamesText, TotalCopiesCount FROM Books";
        var books = new List<(int Id, string Title, string Categories, string Authors, int CopyCount)>();

        using (var cmd = new SqlCommand(getBooksQuery, connection, transaction))
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                books.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4)
                ));
            }
        }

        var insertQuery = @"
            INSERT INTO BookCopies (BookId, Barcode, ShelfLocation, AcquisitionDate, Status,
                                   BookTitleSnapshot, CategoryNamesSnapshot, AuthorNamesSnapshot)
            VALUES (@BookId, @Barcode, @ShelfLocation, GETDATE(), 'AVAILABLE',
                    @BookTitleSnapshot, @CategoryNamesSnapshot, @AuthorNamesSnapshot)";

        var shelfLocations = new[] { "A1", "A2", "B1", "B2", "C1", "C2", "D1", "D2" };
        var copyNumber = 1;

        foreach (var book in books)
        {
            for (int i = 0; i < book.CopyCount; i++)
            {
                using var cmd = new SqlCommand(insertQuery, connection, transaction);
                cmd.Parameters.AddWithValue("@BookId", book.Id);
                cmd.Parameters.AddWithValue("@Barcode", $"BIB-{book.Id:D4}-{copyNumber:D3}");
                cmd.Parameters.AddWithValue("@ShelfLocation", shelfLocations[book.Id % shelfLocations.Length]);
                cmd.Parameters.AddWithValue("@BookTitleSnapshot", book.Title);
                cmd.Parameters.AddWithValue("@CategoryNamesSnapshot", book.Categories);
                cmd.Parameters.AddWithValue("@AuthorNamesSnapshot", book.Authors);
                await cmd.ExecuteNonQueryAsync();
                copyNumber++;
            }
        }
    }

    private async Task SeedTestUserAsync(SqlConnection connection, SqlTransaction transaction)
    {
        // Vérifier si l'utilisateur de test existe déjà
        var checkQuery = "SELECT COUNT(*) FROM library_user WHERE Username = @Username";
        using (var checkCmd = new SqlCommand(checkQuery, connection, transaction))
        {
            checkCmd.Parameters.AddWithValue("@Username", "lecteur");
            var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;
            if (exists) return;
        }

        // Créer l'utilisateur de test
        var passwordHash = _passwordHasher.HashPassword("test123");
        var insertUserQuery = @"
            INSERT INTO library_user (Username, Email, PasswordHash, FirstName, LastName, PhoneNumber, IsActive, CreatedAt, RegistrationDate)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, @PasswordHash, @FirstName, @LastName, @PhoneNumber, 1, GETDATE(), GETDATE())";

        int userId;
        using (var cmd = new SqlCommand(insertUserQuery, connection, transaction))
        {
            cmd.Parameters.AddWithValue("@Username", "lecteur");
            cmd.Parameters.AddWithValue("@Email", "lecteur@bibliotheque.fr");
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@FirstName", "Jean");
            cmd.Parameters.AddWithValue("@LastName", "Dupont");
            cmd.Parameters.AddWithValue("@PhoneNumber", "0612345678");
            userId = (int)await cmd.ExecuteScalarAsync();
        }

        // Attribuer le rôle MEMBER
        var getRoleIdQuery = "SELECT Id FROM role WHERE Name = 'MEMBER'";
        using (var getRoleCmd = new SqlCommand(getRoleIdQuery, connection, transaction))
        {
            var roleId = await getRoleCmd.ExecuteScalarAsync();
            if (roleId != null)
            {
                var insertRoleQuery = "INSERT INTO user_role (UserId, RoleId) VALUES (@UserId, @RoleId)";
                using var insertRoleCmd = new SqlCommand(insertRoleQuery, connection, transaction);
                insertRoleCmd.Parameters.AddWithValue("@UserId", userId);
                insertRoleCmd.Parameters.AddWithValue("@RoleId", (int)roleId);
                await insertRoleCmd.ExecuteNonQueryAsync();
            }
        }
    }
}
