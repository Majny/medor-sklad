using MedorSklad.Data;
using MedorSklad.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Warehouse")));
builder.Services.AddScoped<StockService>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Databáze v souboru: při startu ji vytvoříme a naplníme testovacími daty, pokud je prázdná.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
    db.Database.EnsureCreated();
    DbSeeder.Seed(db);
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();

// Zpřístupnění pro integrační testy (WebApplicationFactory).
public partial class Program;
