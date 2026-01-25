using biblio_project.Services;
using BiblioProject.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

//
// SERVICES (AVANT Build)
//

// MVC
builder.Services.AddControllersWithViews();

// SQL Server (Entity Framework Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// Service de hashage de mot de passe
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

// Service de seeding des données
builder.Services.AddScoped<IDataSeeder, DataSeeder>();

// Authentification par cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
    });

// Autorisation
builder.Services.AddAuthorization();


//
// BUILD
//
var app = builder.Build();


//
// SEEDING DES DONNÉES INITIALES
//
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
    try
    {
        await seeder.SeedAsync();
        Console.WriteLine("Seeding des données terminé avec succès.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erreur lors du seeding des données: {ex.Message}");
    }
}


//
// PIPELINE HTTP
//

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Endpoint test DB (temporaire)
app.MapGet("/db-test", async (AppDbContext db) =>
{
    return await db.Database.CanConnectAsync()
        ? Results.Ok("Connexion SQL Server OK")
        : Results.Problem("Connexion SQL Server échouée");
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Routing MVC
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


//
// RUN
//
app.Run();
